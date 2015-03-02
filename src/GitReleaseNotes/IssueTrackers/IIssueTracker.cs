using System;
using System.Collections.Generic;
using LibGit2Sharp;

namespace GitReleaseNotes.IssueTrackers
{
    public interface IIssueTracker
    {
        bool VerifyArgumentsAndWriteErrorsToConsole();
        IEnumerable<OnlineIssue> GetClosedIssues(DateTimeOffset? since, Reference sinceCommit);
        bool RemotePresentWhichMatches { get; }
        string DiffUrlFormat { get; }
    }
}