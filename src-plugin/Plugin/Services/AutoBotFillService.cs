
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using static K4Arenas.Plugin;

namespace K4Arenas.Services;

public sealed class AutoBotFill
{
    private readonly ISwiftlyCore _core;
    private readonly PlayerManager _playerManager;
    private readonly ArenaManager _arenaManager;
    private readonly Action _terminateRound;

    public AutoBotFill(
        ISwiftlyCore core,
        PlayerManager playerManager,
        ArenaManager arenaManager,
        Action terminateRound)
    {
        _core = core;
        _playerManager = playerManager;
        _arenaManager = arenaManager;
        _terminateRound = terminateRound;

        RegisterEvents();

        _core.Logger.LogInformation("[AutoBotFill] Service initialized.");
    }

    private void RegisterEvents()
    {
        _core.GameEvent.HookPost<EventPlayerSpawn>(OnPlayerSpawn);
        _core.GameEvent.HookPre<EventPlayerDisconnect>(OnPlayerDisconnect);
        _core.GameEvent.HookPost<EventRoundPrestart>(OnRoundPrestart);
        _core.GameEvent.HookPost<EventRoundEnd>(OnRoundEnd);
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn ev)
    {
        var player = ev.UserIdPlayer;

        if (player == null ||
            !player.IsValid ||
            player.IsFakeClient ||
            player.Controller?.IsHLTV == true)
        {
            return HookResult.Continue;
        }

        var arenaPlayer = _playerManager.GetPlayer(player);

        if (arenaPlayer == null || arenaPlayer.IsAfk)
            return HookResult.Continue;

        SpawnBotInEmptyArena(player);

        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect ev)
    {
        var player = _core.PlayerManager.GetPlayer(ev.UserId);

        if (player == null ||
            !player.IsValid ||
            player.Controller?.IsHLTV == true ||
            !player.IsFakeClient)
        {
            return HookResult.Continue;
        }

        ev.DontBroadcast = true;

        return HookResult.Continue;
    }

    private HookResult OnRoundPrestart(EventRoundPrestart ev)
    {
        if (_core.EntitySystem.GetGameRules()?.WarmupPeriod != false)
        {
            _core.Engine.ExecuteCommand("bot_prefix \"WARMUP\"");
            return HookResult.Continue;
        }

        var (_, bots) = GetPlayers();
        var bot = bots.FirstOrDefault();

        if (bot == null)
        {
            _core.Engine.ExecuteCommand("bot_prefix \"\"");
            return HookResult.Continue;
        }

        var arenaPlayer = _playerManager.GetPlayer(bot);
        var arena = arenaPlayer?.CurrentArena;

        string prefix = arena == null
            ? ""
            : arena.Id == -1
                ? "WARMUP"
                : $"{arena.Id + 1} |";

        _core.Engine.ExecuteCommand($"bot_prefix \"{prefix}\"");

        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd ev)
    {
        SpawnBotInEmptyArena(null, roundEnd: true);

        return HookResult.Continue;
    }

    private void SpawnBotInEmptyArena(IPlayer? player, bool roundEnd = false)
    {
        var (players, bots) = GetPlayers();

        var botQuotaMode =
            _core.ConVar.Find<string>("bot_quota_mode")?.Value ?? "none";

        // Even number of real players means no bot is required.
        // AutoBotFill only works with bot_quota_mode "normal".
        if (players.Count % 2 == 0 ||
            !string.Equals(botQuotaMode, "normal", StringComparison.OrdinalIgnoreCase))
        {
            _core.Logger.LogInformation(
                "[AutoBotFill] Even number of players or bot_quota_mode is not normal. No bot required.");

            if (bots.Count > 0)
            {
                _core.Engine.ExecuteCommand("bot_quota 0");
                _core.Engine.ExecuteCommand("bot_prefix \"\"");
            }

            return;
        }

        // The spawning player already has an opponent.
        if (player != null && FindOpponents(player).Any())
            return;

        // Spawn one bot if none exists, or correct the quota if there are too many.
        if (bots.Count == 0 || bots.Count > 1)
        {
            _core.Engine.ExecuteCommand("bot_quota 1");

            _core.Logger.LogInformation(
                "[AutoBotFill] Spawning bot in an empty arena. Players: {Players}, Bots: {Bots}",
                players.Count,
                bots.Count);
        }

        if (roundEnd)
            return;

        // Give the game a moment to create the bot before checking whether
        // the round should be terminated and restarted.
        _core.Scheduler.DelayBySeconds(0.1f, () =>
        {
            var (_, currentBots) = GetPlayers();

            if (currentBots.Count == 0)
                return;

            _terminateRound();
        });
    }

    private (List<IPlayer> Players, List<IPlayer> Bots) GetPlayers()
    {
        var players = new List<IPlayer>();
        var bots = new List<IPlayer>();

        foreach (var player in _core.PlayerManager.GetAllPlayers())
        {
            if (!player.IsValid || player.Controller?.IsHLTV == true)
                continue;

            if (player.IsFakeClient)
            {
                bots.Add(player);
                continue;
            }

            var arenaPlayer = _playerManager.GetPlayer(player);

            if (arenaPlayer != null && !arenaPlayer.IsAfk)
                players.Add(player);
        }

        return (players, bots);
    }

    private IEnumerable<ArenaPlayer> FindOpponents(IPlayer player)
    {
        var arenaPlayer = _playerManager.GetPlayer(player);
        var arena = arenaPlayer?.CurrentArena;

        if (arenaPlayer == null || arena == null)
            return Enumerable.Empty<ArenaPlayer>();

        // Determine which side the player belongs to and return
        // the players from the opposing side.
        if (arena.Team1Players.Any(
                p => p.Player.PlayerID == player.PlayerID))
        {
            return arena.Team2Players.Where(p => p.IsValid);
        }

        if (arena.Team2Players.Any(
                p => p.Player.PlayerID == player.PlayerID))
        {
            return arena.Team1Players.Where(p => p.IsValid);
        }

        return Enumerable.Empty<ArenaPlayer>();
    }
}
