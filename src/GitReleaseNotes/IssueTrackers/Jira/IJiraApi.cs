using System;
using System.Collections.Generic;
using LibGit2Sharp;

namespace GitReleaseNotes.IssueTrackers.Jira
{
    public interface IJiraApi
    {
        IEnumerable<OnlineIssue> GetClosedIssues(GitReleaseNotesArguments arguments, DateTimeOffset? since);
        IEnumerable<OnlineIssue> GetSmartCommitIssues(GitReleaseNotesArguments arguments, IQueryableCommitLog commits, string sinceCommit);
    }
}