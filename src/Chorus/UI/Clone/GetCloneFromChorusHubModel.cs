using System;
using System.Collections.Generic;
using System.IO;
using Chorus.ChorusHub;
using Chorus.VcsDrivers;
using Chorus.VcsDrivers.Mercurial;
using L10NSharp;
using Palaso.Progress;

namespace Chorus.UI.Clone
{
	///<summary>
	/// A class to handle the data passed in and out of the network respository selection dialog (GetCloneFromNetworkFolderDlg).
	///</summary>
	public class GetCloneFromChorusHubModel
	{
		public IEnumerable<RepositoryInformation> HubRepositoryInformation { get; set; }

		public string RepositoryName { get; set; }

		///<summary>
		/// Flag indicating success or otherwise of MakeClone call
		///</summary>
		public bool CloneSucceeded { get; set; }

		/// <summary>
		/// After a successful clone, this will have the path to the folder that we just copied to the computer
		/// </summary>
		public string NewlyClonedFolder { get; private set; }

		// Parent folder to use for cloned repository:
		private readonly string _baseFolder;

		/// <summary>
		/// Use this to inject a custom filter, so that the only projects that can be chosen are ones
		/// your application is prepared to open. The usual method of passing a filter delegate doesn't
		/// work with ChorusHub's cross-process communication, so our ProjectFilter is a string which
		/// gets parsed by the server to determine whether a given mercurial project can be chosen or not.
		/// The default filter is simply empty string, which returns any folder name.
		/// </summary>
		/// <example>Set this to "fileExtension=.lift" to get LIFT repos, but not Bloom ones, for instance.
		/// The server looks in the project's .hg/store/data folder for a file ending in .lift.i</example>
		public string ProjectFilter = string.Empty;

		public GetCloneFromChorusHubModel(string pathToFolderWhichWillContainClonedFolder)
		{
			_baseFolder = pathToFolderWhichWillContainClonedFolder;
		}

		public void MakeClone(IProgress progress)
		{
			 var chorusHubServerInfo = ChorusHubServerInfo.FindServerInformation();
			if (chorusHubServerInfo == null)
			{
				progress.WriteError(LocalizationManager.GetString("Messages.ChorusServerNA", "The Chorus Server is not available."));
				CloneSucceeded = false;
				return;
			}
			if (!chorusHubServerInfo.ServerIsCompatibleWithThisClient)
			{
				progress.WriteError(LocalizationManager.GetString("Messages.ChorusServerIncompatible", "The Chorus Server is not compatible with ths client."));
				CloneSucceeded = false;
				return;
			}

			var targetFolder = Path.Combine(_baseFolder, RepositoryName);
			try
			{
				var client = new ChorusHubClient(chorusHubServerInfo);
				NewlyClonedFolder = HgRepository.Clone(new ChorusHubRepositorySource(RepositoryName, client.GetUrl(RepositoryName), false, HubRepositoryInformation), targetFolder, progress);
				CloneSucceeded = true;
			}
			catch (Exception)
			{
				NewlyClonedFolder = null;
				CloneSucceeded = false;
				throw;
			}
		}

		/// <summary>
		/// Set this to the names of existing projects. Items on the Hub with the same names will be disabled.
		/// </summary>
		public HashSet<string> ExistingProjects { get; set; }

		/// <summary>
		/// Set this to the IDs of existing projects. Items on the Hub with the same IDs will be disabled.
		/// </summary>
		public Dictionary<string, string> ExistingRepositoryIdentifiers { get; set; }

	}
}