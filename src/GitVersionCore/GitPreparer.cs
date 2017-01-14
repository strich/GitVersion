namespace GitVersion
{
	using System;
	using System.IO;
	using System.Linq;
	using GitTools.Git;
	using System.Collections.Generic;

	using LibGit2Sharp;

	using Octokit;
	using System.Text.RegularExpressions;

	public interface IGitPreparer
	{
		string GetWorkingDirectory();

		string GetProjectRootDirectory();
		string GetDotGitDirectory();
		void Initialise(bool normaliseGitDirectory, string currentBranch);
		Repository GetRepository();
		bool IsAPIService();
	}

	public class GitHubPreparer : IGitPreparer
	{
		private string _targetUrl;
		private string _repoOwnerName;
		private string _repoName;
		private GitHubClient _githubClient;
		// TODO - This should probably be the unique user-agent key in this case?
		private static ProductHeaderValue _productHeaderValue = new ProductHeaderValue("GitVersion");
		private Repository _repository;

		public GitHubPreparer(string targetUrl)
		{
			_targetUrl = targetUrl;

			Regex regex = new Regex(@".*\/(.*)\/(.*)");
			var matches = regex.Match(_targetUrl);
			_repoOwnerName = matches.Groups[1].Value;
			_repoName = matches.Groups[2].Value;

			_githubClient = new GitHubClient(_productHeaderValue);
			_githubClient.Credentials = new Octokit.Credentials("s.t.richmond@gmail.com", "powerw00fer");
		}		

		public string GetWorkingDirectory() {
			throw new NotImplementedException();
		}

		public string GetDotGitDirectory()
		{
			throw new NotImplementedException();
		}

		public string GetProjectRootDirectory()
		{
			throw new NotImplementedException();
		}

		public void Initialise(bool normaliseGitDirectory, string currentBranch)
		{
			// TODO: This is all fairly bad - Making shitloads of dupe Commit objects in branches, tags, etc
			// TODO: Auth
			// TODO: There are severe rate-limits for unauth'd reqs or reqs without an App ID. Need to handle this
			// TODO: We're currently getting almost all commits twice - GetAll and Branch.GetAll for each branch. Fix this

			// TODO: Should probably propagate async method sigs all the way up
			var gitHubRepoTask = _githubClient.Repository.Get(_repoOwnerName, _repoName);
			gitHubRepoTask.Wait();
			var gitHubRepo = gitHubRepoTask.Result;

			_repository = new Repository();			

			// TODO: GetAll is paged and probably needs to actually be iterated to get ALL commits
			var gitHubCommitsTask = _githubClient.Repository.Commit.GetAll(_repoOwnerName, _repoName);
			gitHubCommitsTask.Wait();
			var gitHubCommits = gitHubCommitsTask.Result.ToList();

			_repository.Commits = new List<Commit>();
			foreach (var githubCommit in gitHubCommits)
			{
				_repository.Commits.Add(new Commit(githubCommit, gitHubCommits));
			}

			var gitHubBranchesTask = _githubClient.Repository.Branch.GetAll(_repoOwnerName, _repoName);
			gitHubBranchesTask.Wait();
			var gitHubBranches = gitHubBranchesTask.Result.ToList();

			_repository.Branches = new List<Branch>();
			foreach (var gitHubBranch in gitHubBranches)
			{
				var branchCommitsTask = _githubClient.Repository.Commit.GetAll(_repoOwnerName, _repoName, 
					new CommitRequest()
					{
						Sha = gitHubBranch.Name
					});
				branchCommitsTask.Wait();
				var branchCommits = branchCommitsTask.Result.ToList();

				var branch = new Branch(gitHubBranch, branchCommits, gitHubCommits);
				_repository.Branches.Add(branch);

				// Repo head:
				if (gitHubRepo.DefaultBranch == gitHubBranch.Name)
					_repository.Head = branch;
			}

			_repository.Tags = new List<Tag>();
			var gitHubTagsTask = _githubClient.Repository.GetAllTags(_repoOwnerName, _repoName);
			gitHubTagsTask.Wait();
			var gitHubTags = gitHubTagsTask.Result.ToList();
		}

		public Repository GetRepository()
		{
			return _repository;
		}

		public bool IsAPIService()
		{
			return true;
		}
	}

	public class LibGitPreparer : IGitPreparer
	{
        string targetUrl;
        string dynamicRepositoryLocation;
        AuthenticationInfo authentication;
        bool noFetch;
        string targetPath;
		private Repository _repository;

        public LibGitPreparer(string targetPath) : this(null, null, null, false, targetPath) { }
        public LibGitPreparer(string targetUrl, string dynamicRepositoryLocation, Authentication authentication, 
			bool noFetch, string targetPath)
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

		public string GetWorkingDirectory()
		{
            return targetPath;
        }

        public bool IsDynamicGitRepository
        {
            get { return !string.IsNullOrWhiteSpace(DynamicGitRepositoryPath); }
        }

        public string DynamicGitRepositoryPath { get; private set; }

        public void Initialise(bool normaliseGitDirectory, string currentBranch)
        {
			InitializeData();

			if (string.IsNullOrWhiteSpace(targetUrl))
            {
                if (normaliseGitDirectory)
                {
                    GitRepositoryHelper.NormalizeGitDirectory(GetDotGitDirectory(), authentication, noFetch, 
						currentBranch);					
				}
                return;
            }

            var tempRepositoryPath = CalculateTemporaryRepositoryPath(targetUrl, dynamicRepositoryLocation);

            DynamicGitRepositoryPath = CreateDynamicRepository(tempRepositoryPath, authentication, targetUrl, 
				currentBranch, noFetch);			
		}

		private void InitializeData()
		{
			using (var repo = new LibGit2Sharp.Repository(targetPath))
			{
				_repository = new Repository(repo);
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
				var repository = new Repository(possiblePath);                
                return repository.Network.Remotes.Any(r => r.Url == targetUrl);                
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

            var dotGitDirectory = LibGit2Sharp.Repository.Discover(targetPath);

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

                credentials = new UsernamePasswordCredentials
				{
                    Username = authentication.Username,
                    Password = authentication.Password
                };
            }

            Logger.WriteInfo(string.Format("Retrieving git info from url '{0}'", repositoryUrl));

            try
            {
                var cloneOptions = new CloneOptions
				{
                    Checkout = false,
                    CredentialsProvider = (url, usernameFromUrl, types) => credentials
                };
                var returnedPath = LibGit2Sharp.Repository.Clone(repositoryUrl, gitDirectory, cloneOptions);
                Logger.WriteInfo(string.Format("Returned path after repository clone: {0}", returnedPath));
            }
            catch (LibGit2SharpException ex)
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

		public Repository GetRepository()
		{
			return _repository;
		}

		public bool IsAPIService()
		{
			return false;
		}
	}
}