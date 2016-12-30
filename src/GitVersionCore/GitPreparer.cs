namespace GitVersion
{
	using System;
	using System.IO;
	using System.Linq;
	using GitTools.Git;
	using System.Collections.Generic;

	//using LibGit2Sharp;

	public interface IRepository : IDisposable
	{
		IList<Commit> Commits { get; set; }
		Branch Head { get; set; }
		ObjectDatabase ObjectDatabase { get; set; }
		Branch[] Branches { get; set; }
		IList<Tag> Tags { get; set; }
		Branch FindBranch(string branchName);
	}
	public class Repository : IRepository
	{
		private string _dotGitDirectory;
		public Network Network;
		public Branch[] Branches {
			get {
				throw new NotImplementedException();
			}

			set {
				throw new NotImplementedException();
			}
		}

		public Branch Head {
			get {
				throw new NotImplementedException();
			}

			set {
				throw new NotImplementedException();
			}
		}

		public IList<Commit> Commits {
			get {
				throw new NotImplementedException();
			}

			set {
				throw new NotImplementedException();
			}
		}

		public IList<Tag> Tags {
			get {
				throw new NotImplementedException();
			}

			set {
				throw new NotImplementedException();
			}
		}

		public ObjectDatabase ObjectDatabase {
			get {
				throw new NotImplementedException();
			}

			set {
				throw new NotImplementedException();
			}
		}

		public Repository(string dotGitDirectory)
		{
			_dotGitDirectory = dotGitDirectory;
		}

		public void Dispose()
		{
			throw new NotImplementedException();
		}

		public static string Discover(string targetPath)
		{
			throw new NotImplementedException();
		}

		public Branch FindBranch(string branchName)
		{
			throw new NotImplementedException();
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
	}

	public class Tag
	{
		public string Annotation { get; set; }
		public bool IsAnnotated { get; set; }
		public Commit PeeledTarget() { throw new NotImplementedException(); }
		public Commit Target { get; set; }
		public string FriendlyName;
	}

	public class Remote {
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

		public string FriendlyName;
		public string CanonicalName;
		public bool IsRemote;
	}
	public class Commit {
		public string Sha;

		public string Message;

		public IList<Commit> Parents;
		public Committer Committer;
		public DateTimeOffset When() { return Committer.Date; }

		public object Id { get; internal set; }
	}

	public class GitPreparer
    {
        string targetUrl;
        string dynamicRepositoryLocation;
        AuthenticationInfo authentication;
        bool noFetch;
        string targetPath;

        public GitPreparer(string targetPath) : this(null, null, null, false, targetPath) { }
        public GitPreparer(string targetUrl, string dynamicRepositoryLocation, Authentication authentication, bool noFetch, string targetPath)
        {
            this.targetUrl = targetUrl;
            this.dynamicRepositoryLocation = dynamicRepositoryLocation;
            this.authentication = authentication == null ?
                null :
                new AuthenticationInfo
                {
                    Username = authentication.Username,
                    Password = authentication.Password
                };
            this.noFetch = noFetch;
            this.targetPath = targetPath.TrimEnd('/', '\\');
        }

        public string WorkingDirectory
        {
            get { return targetPath; }
        }

        public bool IsDynamicGitRepository
        {
            get { return !string.IsNullOrWhiteSpace(DynamicGitRepositoryPath); }
        }

        public string DynamicGitRepositoryPath { get; private set; }

        public void Initialise(bool normaliseGitDirectory, string currentBranch)
        {
            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                if (normaliseGitDirectory)
                {
                    GitRepositoryHelper.NormalizeGitDirectory(GetDotGitDirectory(), authentication, noFetch, currentBranch);
                }
                return;
            }

            var tempRepositoryPath = CalculateTemporaryRepositoryPath(targetUrl, dynamicRepositoryLocation);

            DynamicGitRepositoryPath = CreateDynamicRepository(tempRepositoryPath, authentication, targetUrl, currentBranch, noFetch);
        }

        public TResult WithRepository<TResult>(Func<IRepository, TResult> action)
        {
            using (IRepository repo = new Repository(GetDotGitDirectory()))
            {
                return action(repo);
            }
        }

        static string CalculateTemporaryRepositoryPath(string targetUrl, string dynamicRepositoryLocation)
        {
            var userTemp = dynamicRepositoryLocation ?? Path.GetTempPath();
            var repositoryName = targetUrl.Split('/', '\\').Last().Replace(".git", string.Empty);
            var possiblePath = Path.Combine(userTemp, repositoryName);

            // Verify that the existing directory is ok for us to use
            if (Directory.Exists(possiblePath))
            {
                if (!GitRepoHasMatchingRemote(possiblePath, targetUrl))
                {
                    var i = 1;
                    var originalPath = possiblePath;
                    bool possiblePathExists;
                    do
                    {
                        possiblePath = string.Concat(originalPath, "_", i++.ToString());
                        possiblePathExists = Directory.Exists(possiblePath);
                    } while (possiblePathExists && !GitRepoHasMatchingRemote(possiblePath, targetUrl));
                }
            }

            return possiblePath;
        }

        static bool GitRepoHasMatchingRemote(string possiblePath, string targetUrl)
        {
            try
            {
                using (var repository = new Repository(possiblePath))
                {
                    return repository.Network.Remotes.Any(r => r.Url == targetUrl);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public string GetDotGitDirectory()
        {
            if (IsDynamicGitRepository)
                return DynamicGitRepositoryPath;

            var dotGitDirectory = Repository.Discover(targetPath);

            if (String.IsNullOrEmpty(dotGitDirectory))
                throw new DirectoryNotFoundException("Can't find the .git directory in " + targetPath);

            dotGitDirectory = dotGitDirectory.TrimEnd('/', '\\');
            if (string.IsNullOrEmpty(dotGitDirectory))
                throw new DirectoryNotFoundException("Can't find the .git directory in " + targetPath);

            return dotGitDirectory;
        }

        public string GetProjectRootDirectory()
        {
            Logger.WriteInfo(string.Format("IsDynamicGitRepository: {0}", IsDynamicGitRepository));
            if (IsDynamicGitRepository)
            {
                Logger.WriteInfo(string.Format("Returning Project Root as {0}", targetPath));
                return targetPath;
            }

            var dotGetGitDirectory = GetDotGitDirectory();
            var result = Directory.GetParent(dotGetGitDirectory).FullName;
            Logger.WriteInfo(string.Format("Returning Project Root from DotGitDirectory: {0} - {1}", dotGetGitDirectory, result));
            return result;
        }

        static string CreateDynamicRepository(string targetPath, AuthenticationInfo authentication, string repositoryUrl, string targetBranch, bool noFetch)
        {
            if (string.IsNullOrWhiteSpace(targetBranch))
            {
                throw new Exception("Dynamic Git repositories must have a target branch (/b)");
            }
            Logger.WriteInfo(string.Format("Creating dynamic repository at '{0}'", targetPath));

            var gitDirectory = Path.Combine(targetPath, ".git");
            if (Directory.Exists(targetPath))
            {
                Logger.WriteInfo("Git repository already exists");
                GitRepositoryHelper.NormalizeGitDirectory(gitDirectory, authentication, noFetch, targetBranch);

                return gitDirectory;
            }

            CloneRepository(repositoryUrl, gitDirectory, authentication);

            // Normalize (download branches) before using the branch
            GitRepositoryHelper.NormalizeGitDirectory(gitDirectory, authentication, noFetch, targetBranch);

            return gitDirectory;
        }

        static void CloneRepository(string repositoryUrl, string gitDirectory, AuthenticationInfo authentication)
        {
			LibGit2Sharp.Credentials credentials = null;
            if (!string.IsNullOrWhiteSpace(authentication.Username) && !string.IsNullOrWhiteSpace(authentication.Password))
            {
                Logger.WriteInfo(string.Format("Setting up credentials using name '{0}'", authentication.Username));

                credentials = new LibGit2Sharp.UsernamePasswordCredentials
				{
                    Username = authentication.Username,
                    Password = authentication.Password
                };
            }

            Logger.WriteInfo(string.Format("Retrieving git info from url '{0}'", repositoryUrl));

            try
            {
                var cloneOptions = new LibGit2Sharp.CloneOptions
				{
                    Checkout = false,
                    CredentialsProvider = (url, usernameFromUrl, types) => credentials
                };
                var returnedPath = LibGit2Sharp.Repository.Clone(repositoryUrl, gitDirectory, cloneOptions);
                Logger.WriteInfo(string.Format("Returned path after repository clone: {0}", returnedPath));
            }
            catch (LibGit2Sharp.LibGit2SharpException ex)
            {
                var message = ex.Message;
                if (message.Contains("401"))
                {
                    throw new Exception("Unauthorised: Incorrect username/password");
                }
                if (message.Contains("403"))
                {
                    throw new Exception("Forbidden: Possbily Incorrect username/password");
                }
                if (message.Contains("404"))
                {
                    throw new Exception("Not found: The repository was not found");
                }

                throw new Exception("There was an unknown problem with the Git repository you provided", ex);
            }
        }
    }
}