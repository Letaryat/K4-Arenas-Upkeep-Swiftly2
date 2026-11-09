using K4ArenaSharedApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Plugins;

namespace K4Arenas;

[PluginMetadata(Id = "k4.arenas", Version = "1.2.6", Name = "K4 - Arenas (PoorVersion)", Author = "K4ryuu", Description = "Ladder type arena gamemode for Counter-Strike: 2 using SwiftlyS2 framework.")]
public sealed partial class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
	private const string ConfigFileName = "config.json";
	private const string ConfigSection = "K4Arenas";

	/// <summary>Static accessor for Core - available to all services</summary>
	public static new ISwiftlyCore Core { get; private set; } = null!;

	/// <summary>Static accessor for Config - available to all services</summary>
	public static IOptionsMonitor<PluginConfig> Config { get; private set; } = null!;

	private PlayerManager _playerManager = null!;
	private ArenaManager _arenaManager = null!;
	private DatabaseService _databaseService = null!;
	private MapService _mapService = null!;
	private DuelService _duelService = null!;
	private AutoBotFillService? _autoBotFillService;
	private PlayerService _playerService = null!;
	private RoundManager _roundManager = null!;
	private TriggerDisciplineService _triggerDisciplineService = null!;
	private CommandsManager _commandsManager = null!;
	private EventsManager _eventsManager = null!;
	private MenuServices _menuServices = null!;

	public override void Load(bool hotReload)
	{
		Core = base.Core;

		LoadConfiguration();

		RoundTypes.LoadFromFiles();

		InitializeServices();

		_eventsManager.RegisterEvents();
		_commandsManager.RegisterCommands();

		if (Config.CurrentValue.UsePredefinedConfig)
		{
			_mapService.ExecCustomConfig();
		}

		if (hotReload)
		{
			HandleHotReload();
		}
	}

	public override void Unload()
	{
		_eventsManager?.UnregisterEvents();
		_playerService?.StopClantagTimer();

		_duelService?.Clear();
		_arenaManager?.Shutdown();
		_playerManager?.Clear();

		_triggerDisciplineService?.ClearAllTriggerShots();
	}

	public override void ConfigureSharedInterface(IInterfaceManager interfaceManager)
	{
		if (_playerManager == null || _arenaManager == null)
			return;

		const string apiVersion = "K4Arena.Api.v1";
		var apiService = new K4ArenaApiService(_playerManager, _arenaManager);
		interfaceManager.AddSharedInterface<IK4ArenaApi, K4ArenaApiService>(apiVersion, apiService);
		Core.Logger.LogInformation("Shared API registered: " + apiVersion);
	}

	private void LoadConfiguration()
	{
		Core.Configuration
			.InitializeJsonWithModel<PluginConfig>(ConfigFileName, ConfigSection)
			.Configure(builder =>
			{
				builder.AddJsonFile(ConfigFileName, optional: false, reloadOnChange: true);
			});

		ServiceCollection services = new();
		services.AddSwiftly(Core)
			.AddOptionsWithValidateOnStart<PluginConfig>()
			.BindConfiguration(ConfigSection);

		var provider = services.BuildServiceProvider();
		Config = provider.GetRequiredService<IOptionsMonitor<PluginConfig>>();
	}

	private void InitializeServices()
	{
		_playerManager = new PlayerManager();
		_arenaManager = new ArenaManager(_playerManager);
		_databaseService = new DatabaseService(Config.CurrentValue.DatabaseConnection, Config.CurrentValue.DatabasePurgeDays);
		_mapService = new MapService();
		_duelService = new DuelService(_playerManager,_arenaManager,UpdatePlayerClantag);
		_playerService = new PlayerService(Core, _playerManager, _databaseService);
		_roundManager = new RoundManager(Core, _playerManager, _arenaManager);
		_triggerDisciplineService = new TriggerDisciplineService(_playerManager);

		_arenaManager.SetDuelService(_duelService);

		if (Config.CurrentValue.EnableAutoBotFill)
		{
			_autoBotFillService = new AutoBotFillService(
				Core,
				_playerManager,
				_arenaManager,
				_roundManager.TerminateRoundIfPossible
			);

			Core.Logger.LogInformation("[K4-Arenas] AutoBotFill enabled.");
		}
		else
		{
			Core.Logger.LogInformation("[K4-Arenas] AutoBotFill disabled.");
		}

		_menuServices = new MenuServices(
			Core,
			_playerManager,
			_arenaManager,
			_databaseService,
			_duelService);

		_duelService.SetMenuServices(_menuServices);

		_commandsManager = new CommandsManager(
			Core,
			_playerManager,
			_arenaManager,
			_databaseService,
			_duelService,
			_playerService,
			_menuServices);

		_eventsManager = new EventsManager(
			Core,
			_playerManager,
			_arenaManager,
			_duelService,
			_databaseService,
			_mapService,
			_playerService,
			_roundManager,
			_triggerDisciplineService);



		Task.Run(async () =>
		{
			await _databaseService.InitializeAsync();
			await _databaseService.PurgeOldRecordsAsync();
		});
	}

	private void UpdatePlayerClantag(ArenaPlayer arenaPlayer) =>
		_playerService.UpdatePlayerClantag(arenaPlayer);

	private void HandleHotReload()
	{
		Core.Scheduler.NextWorldUpdate(() =>
		{
			_arenaManager.Initialize();

			foreach (var player in Core.PlayerManager.GetAllPlayers())
			{
				if (player.IsValid)
				{
					_playerService.SetupPlayer(player);
				}
			}

			Core.Engine.ExecuteCommand("mp_restartgame 1");
		});
	}
}
