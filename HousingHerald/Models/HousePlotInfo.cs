using System;
using System.Globalization;

namespace HousingHerald.Models;

public class HousePlotInfo
{
    public string? DistrictName;
    public int WardId;
    public int PlotId;
    public DateTime? EntryPhaseEndsAt;

    public override string ToString() => $"{DistrictName}, Ward {WardId}, Plot {PlotId}";

    public string GetTpCommand() => $"/li {DistrictName} {WardId} {PlotId}";

    public string GetLocalPhaseEndString()
    {
        if (EntryPhaseEndsAt == null)
            return "Unknown";
        else if (DateTime.Now > EntryPhaseEndsAt)
            return GetResultsPhaseEndsAt()!.Value.ToString(CultureInfo.CurrentCulture);
        else
            return EntryPhaseEndsAt.Value.ToString(CultureInfo.CurrentCulture);
    }

    public DateTime? GetResultsPhaseEndsAt()
    {
        // Results phase is 4 days long
        return EntryPhaseEndsAt?.AddDays(4);
    }
}
