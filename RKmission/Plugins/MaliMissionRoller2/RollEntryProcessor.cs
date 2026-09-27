using AOSharp.Core;
using MaliMissionRoller2;
using System;
using System.Collections.Generic;
using System.Linq;

public class RollEntryProcessor
{
    private const int SPECIAL_CREDIT_ITEM_ID = 297315;
    private const int QL200 = 200;
    private const int NANO_CRYSTAL_QL_TOLERANCE = 10;

    private readonly List<List<int>> _missionLvls;
    private int _missionLevel;

    public RollEntryProcessor(List<List<int>> missionLvls)
    {
        _missionLvls = missionLvls;
    }

    public RollEntryResult ProcessRollEntry(IEnumerable<RollEntryView> rollEntryViews)
    {
        var rollEntry = FindNextRollEntry(rollEntryViews);

        if (rollEntry == null)
        {
            return RollEntryResult.NoValidEntrys();
        }

        if (IsSpecialCreditItem(rollEntry))
        {
            return RollEntryResult.SpecialCredit(rollEntry.RollEntryModel.Ql);
        }

        return ProcessStandardRollEntry(rollEntry, rollEntryViews);
    }

    private RollEntryView FindNextRollEntry(IEnumerable<RollEntryView> rollEntryViews)
    {
        var playerLevel = DynelManager.LocalPlayer.Level;
        var availableMissionLevels = _missionLvls[playerLevel - 1];
        return rollEntryViews.FirstOrDefault(entry => IsEntryRollable(entry, availableMissionLevels, playerLevel));
    }

    private bool IsEntryRollable(RollEntryView entry, List<int> missionLevels, int playerLevel)
    {
        if (IsSpecialCreditItem(entry))
            return true;

        var itemQl = entry.RollEntryModel.Ql;
        var itemName = entry.RollEntryModel.Name;

        // Level 200 and above characters can roll ql 200 items (which aren't nano crystals?)
        if (playerLevel > QL200 && itemQl == QL200 && !IsNanoCrystal(itemName))
            return true;

        // Other scenarios include nano tolerance rolls (+= 10QLs in our case), or an exact match in quality
        return missionLevels.Any(missionLvl => IsQlMatch(itemQl, missionLvl, IsNanoCrystal(itemName)));
    }

    private bool IsQlMatch(int itemQl, int missionLevel, bool isNanoCrystal)
    {
        return isNanoCrystal ? Math.Abs(itemQl - missionLevel) <= NANO_CRYSTAL_QL_TOLERANCE : itemQl == missionLevel;
    }

    private bool IsSpecialCreditItem(RollEntryView entry)
    {
        return entry.RollEntryModel.LowId == SPECIAL_CREDIT_ITEM_ID;
    }

    private bool IsNanoCrystal(string itemName)
    {
        return new[] { "Nano Crystal", "NanoCrystal" }.Any(itemName.Contains);
    }

    private RollEntryResult ProcessStandardRollEntry(RollEntryView entry, IEnumerable<RollEntryView> allEntries)
    {
        var playerLevel = DynelManager.LocalPlayer.Level;
        var itemQl = entry.RollEntryModel.Ql;
        var itemName = entry.RollEntryModel.Name;
        var availableMissionLevels = _missionLvls[playerLevel - 1];

        _missionLevel = DetermineMissionLevel(itemQl, itemName, playerLevel, availableMissionLevels);

        int uniqueItemCount = CountRollableItems(allEntries);
        int sliderValue = availableMissionLevels.IndexOf(_missionLevel) + 1;

        return RollEntryResult.Success(sliderValue, _missionLevel, uniqueItemCount);
    }

    private int DetermineMissionLevel(int itemQl, string itemName, int playerLevel, List<int> missionLevels)
    {
        // If player is level 200+, just grab first >= 200 entry from the table
        return playerLevel > QL200 && itemQl == QL200 && !IsNanoCrystal(itemName) ?
            missionLevels.FirstOrDefault(lvl => lvl >= QL200) :
            missionLevels.OrderBy(lvl => Math.Abs(lvl - itemQl)).First();
    }

    private int CountRollableItems(IEnumerable<RollEntryView> entries)
    {
        return entries.Count(entry =>
        {
            var itemQl = entry.RollEntryModel.Ql;
            var itemName = entry.RollEntryModel.Name;

            if (IsNanoCrystal(itemName))
                return Math.Abs(_missionLevel - itemQl) <= NANO_CRYSTAL_QL_TOLERANCE;

            return _missionLevel == itemQl;
        });
    }
}

public class RollEntryResult
{
    public bool IsSuccess { get; }
    public int SliderValue { get; }
    public int MissionLevel { get; }
    public int UniqueItemCount { get; }
    public int CreditReward { get; }
    public bool IsSpecialCredit { get; }
    public bool NoValidEntry { get; }

    private RollEntryResult(bool isSuccess, int sliderValue, int missionLevel, int uniqueItemCount, int creditReward, bool isSpecialCredit, bool noValidEntry)
    {
        IsSuccess = isSuccess;
        SliderValue = sliderValue;
        MissionLevel = missionLevel;
        UniqueItemCount = uniqueItemCount;
        CreditReward = creditReward;
        IsSpecialCredit = isSpecialCredit;
        NoValidEntry = noValidEntry;
    }

    public static RollEntryResult Success(int sliderValue, int missionLevel, int uniqueItemCount)
    {
        return new RollEntryResult(true, sliderValue, missionLevel, uniqueItemCount, 0, false, false);
    }

    public static RollEntryResult SpecialCredit(int creditReward)
    {
        return new RollEntryResult(true, 0, 0, 0, creditReward, true, false);
    }

    public static RollEntryResult NoValidEntrys()
    {
        return new RollEntryResult(false, 0, 0, 0, 0, false, true);
    }
}