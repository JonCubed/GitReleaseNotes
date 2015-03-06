using System;
using System.Collections.Generic;
using System.Linq;
using LibGit2Sharp;

namespace GitReleaseNotes.IssueTrackers.Jira
{
    public class JiraIssueTracker : IIssueTracker
    {
        private readonly GitReleaseNotesArguments arguments;
        private readonly IJiraApi jiraApi;
        private readonly ILog log;
        private readonly IRepository gitRepository;

        public JiraIssueTracker(IRepository gitRepository, IJiraApi jiraApi, ILog log, GitReleaseNotesArguments arguments)
        {
            this.jiraApi = jiraApi;
            this.log = log;
            this.arguments = arguments;
            this.gitRepository = gitRepository;
        }

        public bool VerifyArgumentsAndWriteErrorsToConsole()
        {
            if (string.IsNullOrEmpty(arguments.JiraServer) ||
                !Uri.IsWellFormedUriString(arguments.JiraServer, UriKind.Absolute))
            {
                log.WriteLine("A valid Jira server must be specified [/JiraServer ]");
                return false;
            }

            // if we have a jql we don't need the project id
            if (string.IsNullOrEmpty(arguments.ProjectId) && string.IsNullOrEmpty(arguments.Jql) && string.IsNullOrEmpty(arguments.SmartCommitsFormat))
            {
                log.WriteLine("/JiraProjectId is a required parameter for Jira");
                return false;
            }

            if (string.IsNullOrEmpty(arguments.Username))
            {
                log.WriteLine("/Username is a required to authenticate with Jira");
                return false;
            }
            if (string.IsNullOrEmpty(arguments.Password))
            {
                log.WriteLine("/Password is a required to authenticate with Jira");
                return false;
            }

            if (string.IsNullOrEmpty(arguments.Jql))
            {
                arguments.Jql = string.Format("project = {0} AND " +
                               "(issuetype = Bug OR issuetype = Story OR issuetype = \"New Feature\") AND " +
                               "status in (Closed, Resolved)", arguments.ProjectId);
            }

            return true;
        }

        public IEnumerable<OnlineIssue> GetClosedIssues(DateTimeOffset? since, string sinceCommit)
        {
            var issues = !string.IsNullOrEmpty(arguments.SmartCommitsFormat)
                            ? jiraApi.GetSmartCommitIssues(arguments, gitRepository.Commits, sinceCommit) 
                            : jiraApi.GetClosedIssues(arguments, since);

            return issues.ToArray();
        }

        public bool RemotePresentWhichMatches { get { return false; }}
        public string DiffUrlFormat { get { return string.Empty; } }
    }
}