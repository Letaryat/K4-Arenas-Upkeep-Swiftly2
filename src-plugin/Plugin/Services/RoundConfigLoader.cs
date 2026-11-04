using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace K4Arenas.Services;

/// <summary>
/// Loads round configs from JSONC files in resources/rounds/
/// </summary>
public static class RoundConfigLoader
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
	};

	/// <summary>Loads all round configs from resources/rounds/</summary>
	public static List<RoundFileConfig> LoadRoundConfigs()
	{
		var configs = new List<RoundFileConfig>();
		var roundsPath = Path.Combine(Core.PluginPath, "resources", "rounds");

		if (!Directory.Exists(roundsPath))
		{
			Core.Logger.LogError("Rounds directory not found at {Path}. No rounds will be loaded.", roundsPath);
			return configs;
		}

		var files = Directory.GetFiles(roundsPath, "*.jsonc");

		if (files.Length == 0)
		{
			Core.Logger.LogWarning("No round configuration files found in {Path}.", roundsPath);
			return configs;
		}

		foreach (var file in files)
		{
			try
			{
				var config = LoadRoundFile(file);
				if (config != null)
					configs.Add(config);
			}
			catch (Exception ex)
			{
				Core.Logger.LogError(ex, "Failed to load round config from {File}", file);
			}
		}

		Core.Logger.LogInformation("Loaded {Count} round configuration(s) from {Path}", configs.Count, roundsPath);
		return configs;
	}

	private static RoundFileConfig? LoadRoundFile(string filePath)
	{
		var jsonContent = File.ReadAllText(filePath);
		return JsonSerializer.Deserialize<RoundFileConfig>(jsonContent, JsonOptions);
	}
}
