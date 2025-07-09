using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System;
using System.Drawing;
using System.Linq;
using System.Numerics;

namespace HousingHerald.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    public MainWindow(Plugin plugin) : base($"Housing Herald###housingherald")
    {
        Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        SizeCondition = ImGuiCond.Once;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(200, 80)
        };

        configuration = plugin.Configuration;

        UpdateLoginNotifsButton();
    }

    public void Dispose() { }

    private void OnLoginNotifsButtonClicked(ImGuiMouseButton button)
    {
        configuration.LoginNotifications = !configuration.LoginNotifications;
        configuration.Save();

        UpdateLoginNotifsButton();
    }

    private void UpdateLoginNotifsButton()
    {
        TitleBarButtons.Clear();

        var bellIcon = configuration.LoginNotifications ? FontAwesomeIcon.Bell : FontAwesomeIcon.BellSlash;
        var tooltip = $"{(configuration.LoginNotifications ? "Disable" : "Enable")} Login Notifications";

        TitleBarButtons.Add(new()
        {
            Click = OnLoginNotifsButtonClicked,
            Icon = bellIcon,
            ShowTooltip = () => ImGui.SetTooltip(tooltip),
        });
    }

    public override void Draw()
    {
        if (configuration.PlayerBid == null)
        {
            ImGui.TextColored(KnownColor.Gray.Vector(), "No current bid!");
        }
        else
        {
            if (ImGui.Selectable(configuration.PlayerBid.ToString()))
            {
                ImGui.SetClipboardText(configuration.PlayerBid.ToString());
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text("Copy to Clipboard");
                ImGui.EndTooltip();
            }
        }

        ImGui.Separator();

        var lotteryPhase = string.Empty;

        if (configuration.PlayerBid == null)
        {
            lotteryPhase = "Unknown";
        }
        else if (DateTime.Now > configuration.PlayerBid.EntryPhaseEndsAt)
        {
            lotteryPhase = "Results";
        }
        else
        {
            lotteryPhase = "Entry";
        }

        ImGui.Text($"{lotteryPhase} Phase (");
        ImGui.SameLine(0f, 0f);

        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.Text(FontAwesomeIcon.Clock.ToIconString());
        ImGui.PopFont();

        ImGui.SameLine(0f, 0f);
        ImGui.Text($" {GetHoursAndMinsRemaining()})");

        if (lotteryPhase == "Results")
        {
            ImGui.Text($"Results Status: ");
            ImGui.SameLine(0f, 0f);
            if (configuration.PlayerCheckedBid == true)
            {
                ImGui.TextColored(KnownColor.Green.Vector(), "Checked!");
            }
            else
            {
                ImGui.TextColored(KnownColor.Red.Vector(), "Unchecked");
                if (configuration.PlayerBid != null && Plugin.PluginInterface.InstalledPlugins.Any(p => p.Name == "Lifestream" && p.IsLoaded == true))
                {
                    if (ImGui.Button("Teleport to Bid"))
                    {
                        Plugin.CommandManager.ProcessCommand(configuration.PlayerBid.GetTpCommand());
                    }
                }
            }
        }
    }

    private string GetHoursAndMinsRemaining()
    {
        if (configuration.PlayerBid?.EntryPhaseEndsAt == null)
        {
            return "N/A";
        }
        else
        {
            var timeToUse = DateTime.Now > configuration.PlayerBid.EntryPhaseEndsAt ?
                configuration.PlayerBid.GetResultsPhaseEndsAt() : configuration.PlayerBid.EntryPhaseEndsAt;
            var timeDiff = timeToUse - DateTime.Now;
            var rounded = TimeSpan.FromMinutes(Math.Ceiling(timeDiff!.Value.TotalMinutes));

            return $"{(int)rounded.TotalHours}:{rounded.Minutes:D2}";
        }
    }
}
