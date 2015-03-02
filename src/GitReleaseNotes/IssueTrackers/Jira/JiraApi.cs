using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using Newtonsoft.Json;

namespace GitReleaseNotes.IssueTrackers.Jira
{
    public class JiraApi : IJiraApi
    {
        public IEnumerable<OnlineIssue> GetClosedIssues(GitReleaseNotesArguments arguments, DateTimeOffset? since)
        {
            string jql;
            if (since.HasValue)
            {
                var sinceFormatted = since.Value.ToString("yyyy-MM-d HH:mm");
                jql = string.Format("{0} AND updated > '{1}'", arguments.Jql, sinceFormatted).Replace("\"", "\\\"");
            }
            else
            {
                jql = arguments.Jql;
            }

            return GetIssuesFromJira(arguments, jql, PrepareOnlineIssue);
        }

        /// <summary>
        /// Gets Issues from smart commits
        /// </summary>
        /// <param name="arguments"></param>
        /// <param name="commitLog"></param>
        /// <param name="sinceCommit"></param>
        /// <returns></returns>
        public IEnumerable<OnlineIssue> GetSmartCommitIssues(GitReleaseNotesArguments arguments,
            IQueryableCommitLog commitLog, ReferenceCollection sinceCommit)
        {
            var regexExp = new Regex(arguments.SmartCommitsFormat, RegexOptions.Multiline | RegexOptions.IgnoreCase);

            var commits = commitLog.QueryBy(new CommitFilter
            {
                SortBy = CommitSortStrategies.Time | CommitSortStrategies.Reverse
            });

            // get project keys from jira
            var projectKeys = GetProjectKeysFromJira(arguments);
            var projectKeyExp = string.Format("({0})-[0-9]+", string.Join("|", projectKeys));

            // get all issues in smart commits
            var smartCommits = commits.Select(c => new { Commit = c, Matches = regexExp.Matches(c.Message)
                                                                                       .Cast<Match>()
                                                                                       .Where(m => Regex.IsMatch(m.Value,projectKeyExp))
                                                                                       .Select(m => m.Value).ToList() })
                                       .Where(x => x.Matches.Count > 0)
                                       .SelectMany(x => x.Matches, (x, y) => new
                                       {
                                            Id = y,
                                            When = x.Commit.Committer.When,
                                            Author = x.Commit.Committer
                                       }
            ).ToList();

            // get retreive info form jira about all issues found in smart commit
            var cache = new Dictionary<string, OnlineIssue>();
            foreach (var smartCommit in smartCommits)
            {
                OnlineIssue jiraIssue;
                if (!cache.ContainsKey(smartCommit.Id))
                {
                    jiraIssue = GetIssueFromJira(arguments, smartCommit.Id);
                    cache.Add(smartCommit.Id, jiraIssue);
                }
                else
                {
                    jiraIssue = cache[smartCommit.Id];
                }
                
                // issue doesnt exist or we don't have permission
                if (jiraIssue == null) continue;

                yield return new OnlineIssue
                {
                    Id = smartCommit.Id,
                    Title = jiraIssue.Title,
                    HtmlUrl = jiraIssue.HtmlUrl,
                    IssueType = jiraIssue.IssueType,
                    DateClosed = smartCommit.When
                };
            }
        }


        private IEnumerable<OnlineIssue> GetIssuesFromJira(GitReleaseNotesArguments arguments, string jql, Func<dynamic, Uri, OnlineIssue> prepareOnlineIssue)
        {
            var baseUrl = new Uri(arguments.JiraServer, UriKind.Absolute);
            var searchUri = new Uri(baseUrl, "rest/api/latest/search");
            var startAt = 0;
            long total;

            var usernameAndPass = string.Format("{0}:{1}", arguments.Username, arguments.Password);
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(usernameAndPass));

            do
            {
                var httpRequest = WebRequest.CreateHttp(searchUri);
                httpRequest.Headers.Add("Authorization", string.Format("Basic {0}", token));
                httpRequest.Method = "POST";
                httpRequest.ContentType = "application/json";

                using (var streamWriter = new StreamWriter(httpRequest.GetRequestStream()))
                {
                    string json = "{\"jql\": \"" + jql + "\",\"startAt\": " + startAt + ", \"maxResults\": 100, \"fields\": [\"summary\",\"issuetype\",\"resolutiondate\"]}";
                    streamWriter.Write(json);
                    streamWriter.Flush();
                    streamWriter.Close();
                }

                var response = (HttpWebResponse)httpRequest.GetResponse();
                if ((int)response.StatusCode == 400)
                {
                    throw new Exception("Jql query error, please review your Jql");
                }

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new Exception("Failed to query Jira: " + response.StatusDescription);
                }

                using (var responseStream = response.GetResponseStream())
                {
                    using (var responseReader = new StreamReader(responseStream))
                    {
                        dynamic responseObject = JsonConvert.DeserializeObject(responseReader.ReadToEnd());

                        // get next start location
                        startAt += responseObject.issues.Count;
                        total = responseObject.total;

                        foreach (var issue in responseObject.issues)
                        {
                            yield return prepareOnlineIssue(issue, baseUrl);
                        }
                    }
                }
            } while (startAt < total); // make sure we loop until we have all results
        }

        public OnlineIssue PrepareOnlineIssue(dynamic issue, Uri baseUrl)
        {
            string summary = issue.fields.summary;
                string id = issue.key;
                string resolutionDate = issue.fields.resolutiondate;

                return new OnlineIssue
                {
                    Id = id,
                    Title = summary,
                    IssueType = IssueType.Issue,
                    HtmlUrl = new Uri(baseUrl, string.Format("browse/{0}", id)),
                    DateClosed =
                        resolutionDate != null
                            ? DateTimeOffset.Parse(resolutionDate)
                            : DateTimeOffset.MinValue
                };
        }

        private IEnumerable<string> GetProjectKeysFromJira(GitReleaseNotesArguments arguments)
        {
            var baseUrl = new Uri(arguments.JiraServer, UriKind.Absolute);
            var searchUri = new Uri(baseUrl, "rest/api/latest/project");

            var usernameAndPass = string.Format("{0}:{1}", arguments.Username, arguments.Password);
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(usernameAndPass));

            var httpRequest = WebRequest.CreateHttp(searchUri);
            httpRequest.Headers.Add("Authorization", string.Format("Basic {0}", token));
            httpRequest.Method = "GET";
            httpRequest.ContentType = "application/json";
                
            var response = (HttpWebResponse)httpRequest.GetResponse();
                
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception("Failed to query Jira: " + response.StatusDescription);
            }

            using (var responseStream = response.GetResponseStream())
            {
                using (var responseReader = new StreamReader(responseStream))
                {
                    dynamic responseObject = JsonConvert.DeserializeObject(responseReader.ReadToEnd());

                    foreach (var project in responseObject)
                    {
                        yield return project.key;
                    }
                }
            }
        }

        private OnlineIssue GetIssueFromJira(GitReleaseNotesArguments arguments, string issueKey)
        {
            var baseUrl = new Uri(arguments.JiraServer, UriKind.Absolute);
            var searchUri = new Uri(baseUrl, string.Format("rest/api/latest/issue/{0}?fields=summary", issueKey));

            var usernameAndPass = string.Format("{0}:{1}", arguments.Username, arguments.Password);
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(usernameAndPass));

            var httpRequest = WebRequest.CreateHttp(searchUri);
            httpRequest.Headers.Add("Authorization", string.Format("Basic {0}", token));
            httpRequest.Method = "GET";
            httpRequest.ContentType = "application/json";

            try
            {
                var response = (HttpWebResponse)httpRequest.GetResponse();
                
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new Exception("Failed to query Jira: " + response.StatusDescription);
                }

                using (var responseStream = response.GetResponseStream())
                {
                    using (var responseReader = new StreamReader(responseStream))
                    {
                        dynamic issue = JsonConvert.DeserializeObject(responseReader.ReadToEnd());
                    
                        return PrepareOnlineIssue(issue, baseUrl);
                    }
                }
            }
            catch (WebException ex)
            {
                var response = (HttpWebResponse) ex.Response;

                if ((int) response.StatusCode == 404) return null;
                
                throw new Exception("Failed to query Jira: " + response.StatusDescription);
            }
        }
    }
}