using SwiftlyS2.Shared.Menu.Components;
using SwiftlyS2.Shared.Players;

namespace K4Arenas;

public partial class MenuServices
{
    public void OpenOpponentMenu(IPlayer challenger)
    {
        var localizer = Core.Translation.GetPlayerLocalizer(challenger);

        var players = Core.PlayerManager.GetAllPlayers()
            .Where(p => p.PlayerID != challenger.PlayerID && _duelService.GetWaitingDuelPlayer(p) != null)
            // For testing with bots:
            //.Where(p => p.PlayerID != challenger.PlayerID)
            .ToList();

        if (players.Count == 0)
        {
            challenger.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.duels.noopponentavailable"]}");
            return;
        }

        var menuBuilder = Core.Menu
            .CreateMenu("k4arenas.duels.opponents")
            .WithTitle(localizer["k4.duelsmenu.opponents"]);

        foreach (var player in players)
        {
            var target = player;
            var targetName = target.Controller?.PlayerName ?? "Player";

            menuBuilder.Add(new ButtonComponent(targetName, _ =>
            {
                Core.Scheduler.NextTick(() =>
                {
                    if (!challenger.IsValid || !target.IsValid)
                        return;

                    OpenWeaponMenu(challenger, target);
                });

                return ValueTask.CompletedTask;
            }));
        }

        var menu = menuBuilder.Build();
        menu.Open(challenger);
    }

    private void OpenWeaponMenu(IPlayer challenger, IPlayer opponent)
    {
        var localizer = Core.Translation.GetPlayerLocalizer(challenger);

        var menuBuilder = Core.Menu
            .CreateMenu("k4arenas.duels.weapons")
            .WithTitle(localizer["k4.duelsmenu.weapontitle", opponent.Controller?.PlayerName ?? "Player"]);

        var roundTypes = RoundTypes.All
            .Where(rt => rt.TeamSize == 1)
            .ToList();

        foreach (var roundType in roundTypes)
        {
            var selectedWeapon = roundType.Name;
            var localizedName = localizer[selectedWeapon];

            menuBuilder.Add(new ButtonComponent(localizedName, _ =>
            {
                Core.Scheduler.NextTick(() =>
                {
                    if (!challenger.IsValid || !opponent.IsValid)
                        return;

                    OpenRoundMenu(challenger, opponent, selectedWeapon);
                });

                return ValueTask.CompletedTask;
            }));
        }

        var menu = menuBuilder.Build();
        menu.Open(challenger);

        challenger.SendChat($"{localizer["k4.general.prefix"]} {localizer["k4.duels.selectWeapon"]}");
    }

    private void OpenRoundMenu(IPlayer challenger, IPlayer opponent, string weaponType)
    {
        var localizer = Core.Translation.GetPlayerLocalizer(challenger);

        var menuBuilder = Core.Menu
            .CreateMenu("k4arenas.duels.rounds")
            .WithTitle(localizer["k4.duelsmenu.roundtitle"]);

        foreach (var selectedRounds in Config.CurrentValue.DuelBORounds
            .Where(r => r > 0)
            .Distinct()
            .OrderBy(r => r))
        {
            var optionText = localizer[
                "k4.duelsmenu.roundoption",
                selectedRounds.ToString().ToUpper()
            ];

            var option = new ButtonComponent(optionText, _ =>
            {
                Core.Scheduler.NextTick(() =>
                {
                    if (!challenger.IsValid || !opponent.IsValid)
                        return;

                    _duelService.StartDuel(challenger, opponent, weaponType, selectedRounds);
                });

                return ValueTask.CompletedTask;
            })
            {
                CloseOnSelect = true
            };

            menuBuilder.Add(option);
        }

        var menu = menuBuilder.Build();
        menu.Open(challenger);
    }
}