using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MDBList.Library;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.Plugin.MDBList.ScheduledTasks;

/// <summary>
/// Phase 2 diagnostics: builds a library snapshot and logs counts + id
/// coverage. Writes nothing to Emby or MDBList. No default trigger -- run
/// manually from the dashboard's Scheduled Tasks page. Temporary: removed
/// once the real sync tasks exist and can report this through the config
/// page instead.
/// </summary>
public class MDBListDiagnosticsTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListDiagnosticsTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="logManager">Instance of the <see cref="ILogManager"/> interface.</param>
    public MDBListDiagnosticsTask(ILibraryManager libraryManager, IUserManager userManager, IUserDataManager userDataManager, ILogManager logManager)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _logger = logManager.GetLogger("MDBList.Diagnostics");
    }

    /// <inheritdoc />
    public string Name => "MDBList Diagnostics";

    /// <inheritdoc />
    public string Key => "MDBListDiagnostics";

    /// <inheritdoc />
    public string Description => "Logs library snapshot counts and id coverage. Writes nothing.";

    /// <inheritdoc />
    public string Category => "MDBList";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return [];
    }

    /// <inheritdoc />
    public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
    {
        var linkedUserConfig = Plugin.Instance?.Configuration.Users.FirstOrDefault();
        if (linkedUserConfig is null)
        {
            _logger.Info("MDBList Diagnostics: no linked user -- connect via the plugin's config page first");
            return Task.CompletedTask;
        }

        var user = _userManager.GetUserById(linkedUserConfig.EmbyUserId);
        if (user is null)
        {
            _logger.Warn("MDBList Diagnostics: linked Emby user {0} no longer exists", linkedUserConfig.EmbyUserId);
            return Task.CompletedTask;
        }

        var snapshot = LibrarySnapshot.Build(_libraryManager, _userDataManager, user);

        _logger.Info(
            "MDBList Diagnostics: {0} movies ({1} unmappable), {2} episodes ({3} unmappable)",
            snapshot.Movies.Count,
            snapshot.UnmappableMovieCount,
            snapshot.Episodes.Count,
            snapshot.UnmappableEpisodeCount);

        var sampleMovie = snapshot.Movies.FirstOrDefault(m => m.LastPlayedDate.HasValue);
        if (sampleMovie is not null)
        {
            _logger.Info(
                "MDBList Diagnostics: sample movie '{0}' LastPlayedDate={1:o} DateCreated={2:o}",
                sampleMovie.Title,
                sampleMovie.LastPlayedDate,
                sampleMovie.DateCreated);
        }

        foreach (var movie in snapshot.Movies)
        {
            _logger.Info(
                "MDBList Diagnostics: movie '{0}' imdb={1} tmdb={2}",
                movie.Title,
                movie.Ids.Imdb,
                movie.Ids.Tmdb);
        }

        progress.Report(100);
        return Task.CompletedTask;
    }
}
