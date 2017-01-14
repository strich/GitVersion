using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

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

		public Committer(Octokit.Committer committer)
		{
			Date = committer.Date;
			Name = committer.Name;
			Email = committer.Email;
		}
	}

	public class Tag
	{
		public string Message;
		public string CanonicalName;
		public string Name;
		public Committer Author;
		public Commit Target;
		public Commit PeeledTarget;

		public Tag(LibGit2Sharp.Repository repo, LibGit2Sharp.Tag tag)
		{
			Message = tag.Annotation == null ? "" : tag.Annotation.Message;
			CanonicalName = tag.CanonicalName;
			Name = tag.FriendlyName;
			Author = tag.Annotation == null ? null : new Committer(tag.Annotation.Tagger);

			Target = new Commit(repo.Commits
				.Where(c => c.Sha == tag.Target.Sha).First());
			PeeledTarget = new Commit(repo.Commits
				.Where(c => c.Sha == tag.PeeledTarget.Sha).First());
		}

		public Tag(Octokit.RepositoryTag tag, IList<Octokit.GitHubCommit> allCommits)
		{
			CanonicalName = tag.Name;
			Name = tag.Name;

			Target = new Commit(allCommits.Single(c => c.Sha == tag.Commit.Sha), allCommits);
			PeeledTarget = Target;
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

	public static class CommitLogExt
	{		 
		public static IEnumerable<Commit> ReachableFrom(this IEnumerable<Commit> commits, Commit commit)
		{
			var reachableParents = new List<Commit>();
			reachableParents.Add(commit);

			foreach (var parent in commit.Parents)
			{
				reachableParents.AddRange(parent.Parents.ReachableFrom(parent));
			}

			return reachableParents;
		}
	}

	public class Branch
	{
		public Commit Tip { get; internal set; }
		public IList<Commit> Commits { get; set; }
		public bool IsTracking { get; set; }

		private bool _isDetachedHead;
		public bool IsDetachedHead() { return _isDetachedHead; }

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
			_isDetachedHead = libgitBranch.CanonicalName.Equals("(no branch)", 
				StringComparison.OrdinalIgnoreCase);
		}

		public Branch(Octokit.Branch gitHubBranch, List<Octokit.GitHubCommit> branchCommits, 
			IList<Octokit.GitHubCommit> allCommits)
		{
			Tip = new Commit(branchCommits.First(), allCommits); // TODO: First or last? Need to check
			Commits = branchCommits.Select(c => new Commit(c, allCommits)).ToList();
			IsTracking = false;
			FriendlyName = gitHubBranch.Name;
			CanonicalName = gitHubBranch.Name;
			IsRemote = true;
			_isDetachedHead = false;
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
	public class Commit : IEquatable<Commit>
	{
		public string Sha;
		public string Message;
		public IList<Commit> Parents;
		public Committer Committer;

		public DateTimeOffset When() { return Committer.Date; }

		public bool Equals(Commit other)
		{
			return Sha == other.Sha;
		}

		public Commit(LibGit2Sharp.Commit libGitCommit)
		{
			Sha = libGitCommit.Sha;
			Message = libGitCommit.Message;
			Parents = libGitCommit.Parents.Select(c => new Commit(c)).ToList();
			Committer = new Committer(libGitCommit.Committer);
		}

		public Commit(Octokit.GitHubCommit gitHubCommit, IList<Octokit.GitHubCommit> allCommits)
		{
			Sha = gitHubCommit.Sha;
			Message = gitHubCommit.Commit.Message;
			Parents = gitHubCommit.Parents.Select(p => 
				new Commit(allCommits.Single(c => c.Sha == p.Sha), allCommits))
				.ToList();
			Committer = new Committer(gitHubCommit.Commit.Committer);
		}
	}

	public static class TopologicalSort
	{
		private static Func<T, IEnumerable<T>> RemapDependencies<T, TKey>(IEnumerable<T> source, Func<T, IEnumerable<TKey>> getDependencies, Func<T, TKey> getKey)
		{
			var map = source.ToDictionary(getKey);
			return item =>
			{
				var dependencies = getDependencies(item);
				return dependencies != null
					? dependencies.Select(key => map[key])
					: null;
			};
		}

		public static IList<T> Sort<T, TKey>(IEnumerable<T> source, Func<T, IEnumerable<TKey>> getDependencies, Func<T, TKey> getKey, bool ignoreCycles = false)
		{
			ICollection<T> source2 = (source as ICollection<T>) ?? source.ToArray();
			return Sort<T>(source2, RemapDependencies(source2, getDependencies, getKey), null, ignoreCycles);
		}

		public static IList<T> Sort<T, TKey>(IEnumerable<T> source, Func<T, IEnumerable<T>> getDependencies, Func<T, TKey> getKey, bool ignoreCycles = false)
		{
			return Sort<T>(source, getDependencies, new GenericEqualityComparer<T, TKey>(getKey), ignoreCycles);
		}

		public static IList<T> Sort<T>(IEnumerable<T> source, Func<T, IEnumerable<T>> getDependencies, IEqualityComparer<T> comparer = null, bool ignoreCycles = false)
		{
			var sorted = new List<T>();
			var visited = new Dictionary<T, bool>(comparer);

			foreach (var item in source)
			{
				Visit(item, getDependencies, sorted, visited, ignoreCycles);
			}

			return sorted;
		}

		public static void Visit<T>(T item, Func<T, IEnumerable<T>> getDependencies, List<T> sorted, Dictionary<T, bool> visited, bool ignoreCycles)
		{
			bool inProcess;
			var alreadyVisited = visited.TryGetValue(item, out inProcess);

			if (alreadyVisited)
			{
				if (inProcess && !ignoreCycles)
				{
					throw new ArgumentException("Cyclic dependency found.");
				}
			}
			else
			{
				visited[item] = true;

				var dependencies = getDependencies(item);
				if (dependencies != null)
				{
					foreach (var dependency in dependencies)
					{
						Visit(dependency, getDependencies, sorted, visited, ignoreCycles);
					}
				}

				visited[item] = false;
				sorted.Add(item);
			}
		}

		public static IList<ICollection<T>> Group<T, TKey>(IEnumerable<T> source, Func<T, IEnumerable<TKey>> getDependencies, Func<T, TKey> getKey, bool ignoreCycles = true)
		{
			ICollection<T> source2 = (source as ICollection<T>) ?? source.ToArray();
			return Group<T>(source2, RemapDependencies(source2, getDependencies, getKey), null, ignoreCycles);
		}

		public static IList<ICollection<T>> Group<T, TKey>(IEnumerable<T> source, Func<T, IEnumerable<T>> getDependencies, Func<T, TKey> getKey, bool ignoreCycles = true)
		{
			return Group<T>(source, getDependencies, new GenericEqualityComparer<T, TKey>(getKey), ignoreCycles);
		}

		public static IList<ICollection<T>> Group<T>(IEnumerable<T> source, Func<T, IEnumerable<T>> getDependencies, IEqualityComparer<T> comparer = null, bool ignoreCycles = true)
		{
			var sorted = new List<ICollection<T>>();
			var visited = new Dictionary<T, int>(comparer);

			foreach (var item in source)
			{
				Visit(item, getDependencies, sorted, visited, ignoreCycles);
			}

			return sorted;
		}

		public static int Visit<T>(T item, Func<T, IEnumerable<T>> getDependencies, List<ICollection<T>> sorted, Dictionary<T, int> visited, bool ignoreCycles)
		{
			const int inProcess = -1;
			int level;
			var alreadyVisited = visited.TryGetValue(item, out level);

			if (alreadyVisited)
			{
				if (level == inProcess && ignoreCycles)
				{
					throw new ArgumentException("Cyclic dependency found.");
				}
			}
			else
			{
				visited[item] = (level = inProcess);

				var dependencies = getDependencies(item);
				if (dependencies != null)
				{
					foreach (var dependency in dependencies)
					{
						var depLevel = Visit(dependency, getDependencies, sorted, visited, ignoreCycles);
						level = Math.Max(level, depLevel);
					}
				}

				visited[item] = ++level;
				while (sorted.Count <= level)
				{
					sorted.Add(new Collection<T>());
				}
				sorted[level].Add(item);
			}

			return level;
		}

	}

	public class GenericEqualityComparer<TItem, TKey> : EqualityComparer<TItem>
	{
		private readonly Func<TItem, TKey> getKey;
		private readonly EqualityComparer<TKey> keyComparer;

		public GenericEqualityComparer(Func<TItem, TKey> getKey)
		{
			this.getKey = getKey;
			keyComparer = EqualityComparer<TKey>.Default;
		}

		public override bool Equals(TItem x, TItem y)
		{
			if (x == null && y == null)
			{
				return true;
			}
			if (x == null || y == null)
			{
				return false;
			}
			return keyComparer.Equals(getKey(x), getKey(y));
		}

		public override int GetHashCode(TItem obj)
		{
			if (obj == null)
			{
				return 0;
			}
			return keyComparer.GetHashCode(getKey(obj));
		}
	}
}
