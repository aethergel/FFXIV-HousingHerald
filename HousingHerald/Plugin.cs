using Dalamud.Game.Addon.Events;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Memory;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using HousingHerald.Models;
using HousingHerald.Windows;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace HousingHerald;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IAddonEventManager AddonEventManager { get; private set; } = null!;

    private const string MainCommandName = "/pherald";
    private const string AddonSelectYesNoTextScrollName = "SelectYesNoTextScroll";
    private const string AddonSelectOkName = "SelectOk";
    private const string AddonHousingSignBoardName = "HousingSignBoard";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("HousingHerald");
    private MainWindow MainWindow { get; init; }

    private readonly DalamudLinkPayload bidTeleportPayload;
    private IAddonEventHandle? clickCancelEventHandle;
    private IAddonEventHandle? clickOkEventHandle;
    private bool housingSignBoardOpen = false;
    public HousePlotInfo? currentlyViewedPlot = null;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(MainCommandName, new CommandInfo(OnMainCommand)
        {
            HelpMessage = "Opens the menu for Housing Herald."
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
        ClientState.Login += OnLogin;

        bidTeleportPayload = PluginInterface.AddChatLinkHandler(0, OnBidTeleportLinkClick);

        // Bid Confirmation Window opens
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, AddonSelectYesNoTextScrollName, OnSelectYesNoTextScrollPostSetup);
        // Bid Confirmation Window closes
        AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonSelectYesNoTextScrollName, OnSelectYesNoTextScrollPreFinalize);

        // Bid Result Window opens
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, AddonSelectOkName, OnSelectOkPostSetup);
        // Bid Result Window closes
        AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonSelectOkName, OnSelectOkPreFinalize);

        // Housing Placard Window opens
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, AddonHousingSignBoardName, OnHousingSignBoardPostSetup);
        // Housing Placard Window receives update
        AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, AddonHousingSignBoardName, OnHousingSignBoardPostRequestedUpdate);
        // Housing Placard Window closes
        AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonHousingSignBoardName, OnHousingSignBoardPreFinalize);

        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, OnAnyAddonSetup);

        Log.Debug("Registered listeners successfully!");
    }

    private void OnLogin()
    {
        if (Configuration.LoginNotifications == true)
        {
            CheckBidStatus();
        }
    }

    public void Dispose()
    {
        ClientState.Login -= OnLogin;
        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();
        CommandManager.RemoveHandler(MainCommandName);
        PluginInterface.RemoveChatLinkHandler();

        if (clickCancelEventHandle != null)
        {
            AddonEventManager.RemoveEvent(clickCancelEventHandle);
            clickCancelEventHandle = null;
        }

        if (clickOkEventHandle != null)
        {
            AddonEventManager.RemoveEvent(clickOkEventHandle);
            clickOkEventHandle = null;
        }

        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, AddonSelectYesNoTextScrollName, OnSelectYesNoTextScrollPostSetup);
        AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, AddonSelectYesNoTextScrollName, OnSelectYesNoTextScrollPreFinalize);
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, AddonSelectOkName, OnSelectOkPostSetup);
        AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, AddonSelectOkName, OnSelectOkPreFinalize);
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, AddonHousingSignBoardName, OnHousingSignBoardPostSetup);
        AddonLifecycle.UnregisterListener(AddonEvent.PostRequestedUpdate, AddonHousingSignBoardName, OnHousingSignBoardPostRequestedUpdate);
        AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, AddonHousingSignBoardName, OnHousingSignBoardPreFinalize);
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, OnAnyAddonSetup);
    }

    private void OnBidTeleportLinkClick(uint cmdId, SeString seString)
    {
        var bid = Configuration.PlayerBid;

        if (bid == null)
        {
            Log.Error("'PlayerBid' is null!");
            return;
        }

        CommandManager.ProcessCommand(bid.GetTpCommand());
    }

    private void OnMainCommand(string command, string args)
    {
        ToggleMainUI();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleMainUI() => MainWindow.Toggle();

    private void OnAnyAddonSetup(AddonEvent eventType, AddonArgs args)
    {
        Log.Debug($"Addon setup: {args.AddonName}");
    }

    #region Listeners
    // Listeners //////////////////////////////////////////////////////////////////////////////////////////////////////

    private unsafe void OnSelectYesNoTextScrollPostSetup(AddonEvent eventType, AddonArgs args)
    {
        if (housingSignBoardOpen == false)
            return;

        var addon = (AtkUnitBase*)args.Addon;

        // NodeId for "Confirm" button is 5
        // NodeId for "Cancel" button is 6
        const uint ConfirmButtonNodeId = 6;

        var compNode = addon->GetComponentNodeById(ConfirmButtonNodeId);
        if (compNode == null)
        {
            Log.Error("'Confirm' button node not found.");
            return;
        }

        Log.Debug($"Attaching handler to 'Confirm' button via NodeId {ConfirmButtonNodeId}.");
        clickCancelEventHandle = AddonEventManager.AddEvent(
            (nint)addon,
            (nint)compNode,
            AddonEventType.ButtonClick,
            OnConfirmClicked
        );

        if (clickCancelEventHandle != null)
            Log.Debug("'clickCancelEventHandle' attached successfully.");
        else
            Log.Error("Failed to attach 'clickCancelEventHandle'.");
    }

    private unsafe void OnSelectYesNoTextScrollPreFinalize(AddonEvent eventType, AddonArgs args)
    {
        if (clickCancelEventHandle != null)
        {
            AddonEventManager.RemoveEvent(clickCancelEventHandle);
            clickCancelEventHandle = null;
            Log.Debug("'clickCancelEventHandle' removed successfully.");
        }
    }

    private unsafe void OnSelectOkPostSetup(AddonEvent eventType, AddonArgs args)
    {
        if (housingSignBoardOpen == false)
            return;

        var addon = (AtkUnitBase*)args.Addon;

        // NodeId for "Ok" button is ???
        const uint OkButtonNodeId = 1;

        var compNode = addon->GetComponentByNodeId(OkButtonNodeId);
        if (compNode == null)
        {
            Log.Error("'Ok' button node not found.");
            return;
        }

        Log.Debug($"Attaching handler to 'Ok' button via NodeId {OkButtonNodeId}.");
        clickOkEventHandle = AddonEventManager.AddEvent(
            (nint)addon,
            (nint)compNode,
            AddonEventType.ButtonClick,
            OnOkClicked
        );

        if (clickOkEventHandle != null)
            Log.Debug("'clickOkEventHandle' attached successfully.");
        else
            Log.Error("Failed to attach 'clickOkEventHandle'.");
    }

    private unsafe void OnSelectOkPreFinalize(AddonEvent eventType, AddonArgs args)
    {
        if (clickOkEventHandle != null)
        {
            AddonEventManager.RemoveEvent(clickOkEventHandle);
            clickOkEventHandle = null;
            Log.Debug("'clickOkEventHandle' removed successfully.");
        }
    }

    private unsafe void OnHousingSignBoardPostSetup(AddonEvent eventType, AddonArgs args)
    {
        Log.Debug("Housing placard opened!");
        housingSignBoardOpen = true;
    }

    private unsafe void OnHousingSignBoardPostRequestedUpdate(AddonEvent eventType, AddonArgs args)
    {
        Log.Debug("Housing placard update received!");

        var addon = (AtkUnitBase*)args.Addon;

        // NodeId for "Address" text is 56
        const uint AddressTextNodeId = 56;
        // NodeId for "Plot Details" text is 64
        const uint PlotDetailsTextNodeId = 64;

        var addressTextNode = addon->GetTextNodeById(AddressTextNodeId);
        if (addressTextNode == null)
        {
            Log.Error($"Address text node not found.");
            return;
        }

        var addressText = MemoryHelper.ReadSeString(&addressTextNode->NodeText).TextValue;

        Log.Debug($"Address text: {addressText}");

        var addressInfo = ParseAddressInfo(addressText);
        if (addressInfo == null)
        {
            return;
        }

        var plotDetailsTextNode = addon->GetTextNodeById(PlotDetailsTextNodeId);
        if (plotDetailsTextNode == null)
        {
            Log.Error($"Plot Details text node not found.");
            return;
        }

        var plotDetailsText = MemoryHelper.ReadSeString(&plotDetailsTextNode->NodeText).TextValue;

        Log.Debug($"Plot Details text: {plotDetailsText}");

        var endTime = ParsePhaseEndUnixTimestamp(plotDetailsText);
        if (endTime == null)
        {
            return;
        }

        addressInfo.EntryPhaseEndsAt = endTime.Value;

        currentlyViewedPlot = addressInfo;
    }

    private unsafe void OnHousingSignBoardPreFinalize(AddonEvent eventType, AddonArgs args)
    {
        Log.Debug("Housing placard closed!");
        housingSignBoardOpen = false;
        currentlyViewedPlot = null;
    }

    #endregion

    #region Events
    // Events /////////////////////////////////////////////////////////////////////////////////////////////////////////

    private void OnConfirmClicked(AddonEventType type, AddonEventData data)
    {
        Log.Debug("User clicked 'Confirm' on the housing bid confirmation.");

        if (currentlyViewedPlot == null)
        {
            Log.Error("'currentlyViewedPlot' is null!");
            return;
        }

        Configuration.PlayerBid = currentlyViewedPlot;
        Configuration.Save();
    }

    private void OnOkClicked(AddonEventType type, AddonEventData data)
    {
        Log.Debug("User clicked 'Ok' on the housing results confirmation.");

        Configuration.PlayerCheckedBid = true;
        Configuration.Save();
    }

    #endregion

    private void CheckBidStatus()
    {
        var playerBid = Configuration.PlayerBid;

        if (playerBid == null)
        {
            return;
        }

        // If it's currently past when the results phase was supposed to end, clear out old bid
        if (DateTime.Now > playerBid.GetResultsPhaseEndsAt())
        {
            Chat.Print($"[Housing Herald] The Results Phase is over, your tracked bid has been reset.");
            Configuration.PlayerBid = null;
            Configuration.PlayerCheckedBid = false;
            Configuration.Save();
        }
        // If it's currently past when the Entry Phase was supposed to end, notify them that their bid is ready
        else if (DateTime.Now > playerBid.EntryPhaseEndsAt)
        {
            if (PluginInterface.InstalledPlugins.Any(p => p.Name == "Lifestream" && p.IsLoaded == true))
            {
                Chat.Print(new SeString(
                    new TextPayload($"[Housing Herald] The results are in! Your bid on "),
                    new UIForegroundPayload(710),
                    bidTeleportPayload,
                    new TextPayload($"[{playerBid}]"),
                    RawPayload.LinkTerminator,
                    new UIForegroundPayload(0),
                    new TextPayload($" can be checked until {playerBid.GetLocalPhaseEndString()}."))
                );
            }
            else
            {
                Chat.Print($"[Housing Herald] The results are in! Your bid on {playerBid} can be checked until {playerBid.GetLocalPhaseEndString()}.");
            }
        }
        // If it's not past when the Entry Phase was supposed to end, tell the player when the Results Phase starts
        else
        {
            Chat.Print($"[Housing Herald] You have already bid on {playerBid}. The Results Phase starts at {playerBid.GetLocalPhaseEndString()} - good luck!");
        }
    }

    private HousePlotInfo? ParseAddressInfo(string text)
    {
        // Expected format: "Plot 53, 26th Ward, Shirogane"
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            Log.Error("Could not parse address info!");
            return null;
        }

        var plotMatch = Regex.Match(parts[0], @"Plot\s+(\d+)");
        var wardMatch = Regex.Match(parts[1], @"(\d+)[a-z]{2}\s+Ward", RegexOptions.IgnoreCase);
        var districtName = parts[2];

        if (!plotMatch.Success || !wardMatch.Success)
        {
            Log.Error("Could not parse address info!");
            return null;
        }

        var addressInfo = new HousePlotInfo
        {
            PlotId = int.Parse(plotMatch.Groups[1].Value),
            WardId = int.Parse(wardMatch.Groups[1].Value),
            DistrictName = districtName
        };

        Log.Debug($"Parsed: {addressInfo}");
        return addressInfo;
    }

    private static readonly Regex PhaseEndRegex = new(
    @"(?<hour>\d{1,2}):(?<minute>\d{2})\s*(?<ampm>a\.m\.|p\.m\.)\s*(?<month>\d{1,2})/(?<day>\d{1,2})/(?<year>\d{4})",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static DateTime? ParsePhaseEndUnixTimestamp(string text)
    {
        // Expected format: "10:59 a.m. 7/8/2025"
        var match = PhaseEndRegex.Match(text);
        if (!match.Success)
            return null;

        try
        {
            var hour = int.Parse(match.Groups["hour"].Value);
            var minute = int.Parse(match.Groups["minute"].Value);
            var ampm = match.Groups["ampm"].Value;
            var month = int.Parse(match.Groups["month"].Value);
            var day = int.Parse(match.Groups["day"].Value);
            var year = int.Parse(match.Groups["year"].Value);

            if (ampm == "p.m." && hour < 12)
                hour += 12;
            else if (ampm == "a.m." && hour == 12)
                hour = 0;

            var localTime = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local);
            Log.Debug($"Parsed: {localTime}");
            return localTime;
        }
        catch
        {
            return null;
        }
    }
}
