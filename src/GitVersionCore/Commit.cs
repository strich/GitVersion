using System;
using System.Collections.Generic;
using System.Linq;

namespace GitVersion
{
	public interface IRepository
	{
		IList<Commit> Commits { get; set; }
		Branch Head { get; set; }
		ObjectDatabase ObjectDatabase { get; set; }
		IList<Branch> Branches { get; set; }
		IList<Tag> Tags { get; set; }
		Branch FindBranch(string branchName);
		string GetRepositoryDirectory();
	}
	public class Repository : IRepository
	{
		private string _dotGitDirectory;
		public Network Network;

		private IList<Branch> _branches;
		public IList<Branch> Branches {
			get { return _branches; }
			set { _branches = value; }
		}

		private Branch _head;
		public Branch Head {
			get { return _head; }
			set { _head = value; }
		}

		private IList<Commit> _commits;
		public IList<Commit> Commits {
			get { return _commits; }
			set { _commits = value; }
		}

		private IList<Tag> _tags;
		public IList<Tag> Tags {
			get { return _tags; }
			set { _tags = value; }
		}

		public ObjectDatabase ObjectDatabase {
			get {
				throw new NotImplementedException();
			}

			set {
				throw new NotImplementedException();
			}
		}

		public Repository() { }

		public Repository(LibGit2Sharp.Repository repo)
		{
			_head = new Branch(repo.Head);
			_branches = repo.Branches.Select(b => new Branch(b)).ToList();
			_commits = repo.Commits.Select(c => new Commit(c)).ToList();
			_tags = repo.Tags.Select(t => new Tag(repo, t)).ToList();
		}

		public Repository(string dotGitDirectory)
		{
			_dotGitDirectory = dotGitDirectory;
		}

		public Branch FindBranch(string branchName)
		{
			throw new NotImplementedException();
		}

		public string GetRepositoryDirectory()
		{
			// TODO - Seems to only be used by depreciated code?
			return "";
		}
	}

	public class ObjectDatabase
	{
		public Commit FindMergeBase(Commit first, Commit second) { throw new NotImplementedException(); }
	}

	public class Committer
	{
		public DateTimeOffset Date { get; set; }
		public string Name { get; set; }
		public string Email { get; set; }

		public Committer(LibGit2Sharp.Signature committer)
		{
			Date = committer.When;
			Name = committer.Name;
			Email = committer.Email;
		}
	}

	public class Tag
	{
		public string Message;
		public string Name;
		public Committer Author;
		public Commit Target;
		public Commit PeeledTarget;

		public Tag(LibGit2Sharp.Repository repo, LibGit2Sharp.Tag tag)
		{
			Message = tag.Annotation.Message;
			Name = tag.Annotation.Name;
			Target = new Commit(repo.Commits
				.Where(c => c.Sha == tag.Target.Sha).First());
			PeeledTarget = new Commit(repo.Commits
				.Where(c => c.Sha == tag.PeeledTarget.Sha).First());
			Author = new Committer(tag.Annotation.Tagger);
		}
	}

	public class Remote
	{
		public string Url;
	}
	public class Network
	{
		public IList<Remote> Remotes;
	}
	public class Branch
	{
		public Commit Tip { get; internal set; }
		public IList<Commit> Commits { get; set; }
		public bool IsTracking { get; internal set; }

		public bool IsDetachedHead() { throw new NotImplementedException(); }

		public string FriendlyName;
		public string CanonicalName;
		public bool IsRemote;

		public IEnumerable<Commit> CommitsPriorToThan(DateTimeOffset olderThan)
		{
			return Commits.SkipWhile(c => c.When() > olderThan);
		}

		public Branch(LibGit2Sharp.Branch libgitBranch)
		{
			Tip = new Commit(libgitBranch.Tip);
			Commits = libgitBranch.Commits.Select(c => new Commit(c)).ToList();
			IsTracking = libgitBranch.IsTracking;
			FriendlyName = libgitBranch.FriendlyName;
			CanonicalName = libgitBranch.CanonicalName;
			IsRemote = libgitBranch.IsRemote;			
		}

		/// <summary>
		/// Checks if the two branch objects refer to the same branch (have the same friendly name).
		/// </summary>
		public bool IsSameBranch(Branch otherBranch)
		{
			// For each branch, fixup the friendly name if the branch is remote.
			var otherBranchFriendlyName = otherBranch.IsRemote ?
				otherBranch.FriendlyName.Substring(otherBranch.FriendlyName.IndexOf("/", StringComparison.Ordinal) + 1) :
				otherBranch.FriendlyName;
			var branchFriendlyName = IsRemote ?
				FriendlyName.Substring(FriendlyName.IndexOf("/", StringComparison.Ordinal) + 1) :
				FriendlyName;

			return otherBranchFriendlyName == branchFriendlyName;
		}
	}
	public class Commit
	{
		public string Sha;
		public string Message;
		public IList<Commit> Parents;
		public Committer Committer;

		public DateTimeOffset When() { return Committer.Date; }

		public Commit(LibGit2Sharp.Commit libGitCommit)
		{
			Sha = libGitCommit.Sha;
			Message = libGitCommit.Message;
			Parents = libGitCommit.Parents.Select(c => new Commit(c)).ToList();
			Committer = new Committer(libGitCommit.Committer);
		}
		
	}
}
