using Dalamud.Configuration;
using HousingHerald.Models;
using System;

namespace HousingHerald;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    public HousePlotInfo? PlayerBid { get; set; } = null;
    public bool LoginNotifications { get; set; } = true;
    public bool PlayerCheckedBid { get; set; } = false;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
