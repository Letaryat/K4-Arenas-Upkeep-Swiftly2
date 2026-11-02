> [!IMPORTANT]  
> [K4ryuu](https://github.com/K4ryuu) is the creator of this plugin.<br>
> Since he is no longer maintaining his CS2 plugins, I forked the fork from [Shmitzas](https://github.com/shmitzas/K4-Arenas-Upkeep). New features are mostly vibecoded but tested.

<div align="center">
  <h3 align="center">K4 - Arenas (PoorVersion)</h3>
  <a align="center">An all-in-one arena plugin for Counter-Strike 2 with ladder-style gameplay. Supports any map, 2v2/3v3 modes, weapon preferences and a developer API for custom round types.</a>
</div>

<div align="center">
    <p>Support further vibecoded development</p>
    <h2>
        <a href="https://discord.com/invite/mEmdyqM3Um" target="_blank"><img src="https://img.shields.io/badge/Discord%20Server-7289da?style=for-the-badge&logo=discord&logoColor=white" /></a>
        <a href="https://ko-fi.com/letaryat" target="_blank"><img src="https://img.shields.io/badge/Ko--fi-F16061?logo=ko-fi&logoColor=white&style=for-the-badge" /></a>
    </h2>
</div>

### Dependencies

- [**SwiftlyS2**](https://github.com/swiftly-solution/swiftlys2): Server plugin framework for Counter-Strike 2
- **Database**: One of the following supported databases:
  - **MySQL / MariaDB** - Recommended for production
  - **PostgreSQL** - Full support
  - **SQLite** - Great for single-server setups

<p align="right">(<a href="#readme-top">back to top</a>)</p>

<!-- INSTALLATION -->

## Installation

1. Install [SwiftlyS2](https://github.com/swiftly-solution/swiftlys2) on your server
2. Configure your database connection in SwiftlyS2's `database.jsonc` (MySQL, PostgreSQL, or SQLite)
3. [Download the latest release](https://github.com/shmitzas/K4-Arenas-Upkeep/releases/latest)
4. Extract to your server's `swiftlys2/plugins/` directory
5. Configure `config.json` in the plugin folder
6. Restart your server - database tables will be created automatically

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Configuration

| Option               | Description                                      | Default    |
| -------------------- | ------------------------------------------------ | ---------- |
| `DatabaseConnection` | Database connection name for storing preferences | `""`       |
| `DatabasePurgeDays`  | Days to purge information in k4-arenas tables         | `30`        |
| `UsePredefinedCOnfig`   | Use settings that plugin will automatically use for proper multiarena experience                         | `true`       |
| `GunsCommands`    | Command to change guns preferences | `"guns", "gunpref"....`    |
| `RoundsCommands`    | Command to change rounds preferences | `"rounds", "roundspref"`    |
| `QueueCommands`    | Command to add player to queue | `"queue"`    |
| `ArenaAfkCommand`    | Command to toggle AFK status | `"afk"`    |
| `DuelCommands`    | Command to challenge player to specific round type and BO(X) duel | `"duel"`    |
| `DuelAcceptCommands`    | Command to accept duel | `"daccept"`    |
| `DuelCancelCommands`    | Command to cancel pending request for a duel | `"dcancel"`    |
| `EnableDuelService`    | Enable duel service                     | `"true"`    |
| `DuelBORounds`    | Duel BO Rounds                     | `1,2,3,4,5,6,7,8`    |
| `EnableAutoBotFill`    | Enable auto bot fill when players are not even                     | `false`    |

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Commands

| Command   | Description                      |
| --------- | -------------------------------- |
| `!guns`   | Open weapon preferences menu     |
| `!rounds` | Open round type preferences menu |
| `!queue`    | Toggle queue status                |
| `!afk`    | Toggle AFK status                |
| `!duel`    | Opens menu to request player for a challenge                 |
| `!daccept`    | Accept pending request for a duel                |
| `!dcancel`    | Deny pending request for a duel                |

<p align="right">(<a href="#readme-top">back to top</a>)</p>

---

## Database

The plugin uses automatic schema management with FluentMigrator. Tables are created automatically on first run.

### Supported Databases

| Database        | Status  | Notes                                      |
| --------------- | ------- | ------------------------------------------ |
| MySQL / MariaDB | ✅ Full | Recommended for multi-server setups        |
| PostgreSQL      | ✅ Full | Alternative for existing Postgres setups   |
| SQLite          | ✅ Full | Perfect for single-server, no setup needed |

### Database Tables

- `k4_arenas_players` - Player records and last seen timestamps
- `k4_arenas_weapons` - Player weapon preferences per weapon type
- `k4_arenas_rounds` - Player round type preferences

<p align="right">(<a href="#readme-top">back to top</a>)</p>

<!-- LICENSE -->

## License

Distributed under the GPL-3.0 License. See [`LICENSE.md`](LICENSE.md) for more information.

<p align="right">(<a href="#readme-top">back to top</a>)</p>
