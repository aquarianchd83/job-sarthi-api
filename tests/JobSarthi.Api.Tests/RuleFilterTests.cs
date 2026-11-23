using JobSarthi.Api.Domain.Entities;
using JobSarthi.Api.Domain.Enums;
using JobSarthi.Api.Filtering;
using Xunit;

namespace JobSarthi.Api.Tests;

public class RuleFilterTests
{
    private static Rule MyRules() => new()
    {
        Roles = { "Senior .NET Developer", "Full Stack Developer" },
        MinExperience = 5, MaxExperience = 12,
        // Hybrid or onsite in my own city, remote everywhere else.
        LocationPrefs = { new LocationPref { Place = "Pune", Modes = { "Hybrid", "Onsite" } } },
        OtherModes = new(),
        Keywords = { "C#", "Angular" },
        BlacklistedCompanies = { "BadCorp" },
    };

    private static Job J(string title, string company = "Acme", string loc = "Pune", WorkMode mode = WorkMode.Onsite, string desc = "Great role.") =>
        new() { Title = title, Company = company, Location = loc, WorkMode = mode, Description = desc };

    [Fact] public void Blacklisted_company_is_dropped() =>
        Assert.StartsWith("Blacklisted", RuleFilter.Check(J("Senior .NET Developer", "BadCorp Ltd"), MyRules()));

    [Fact] public void Engineer_counts_as_developer() =>
        Assert.Null(RuleFilter.Check(J("Senior .NET Engineer"), MyRules()));

    [Fact] public void Unrelated_title_is_dropped() =>
        Assert.Equal("Title does not match your roles", RuleFilter.Check(J("Senior UX Designer"), MyRules()));

    [Fact] public void Full_stack_with_and_without_hyphen_match()
    {
        Assert.Null(RuleFilter.Check(J("Full-Stack Developer"), MyRules()));
        Assert.Null(RuleFilter.Check(J("Full Stack Developer"), MyRules()));
    }

    // My rules: Pune = Hybrid or Onsite. Everywhere else = Remote only (OtherModes starts empty, so add Remote).
    private static Rule PuneHybridElsewhereRemote() { var r = MyRules(); r.OtherModes = new() { "Remote" }; return r; }

    [Fact] public void Onsite_or_hybrid_in_my_city_passes()
    {
        Assert.Null(RuleFilter.Check(J("Senior .NET Developer", loc: "Pune, India", mode: WorkMode.Onsite), PuneHybridElsewhereRemote()));
        Assert.Null(RuleFilter.Check(J("Senior .NET Developer", loc: "Pune", mode: WorkMode.Hybrid), PuneHybridElsewhereRemote()));
    }

    [Fact] public void Remote_in_my_city_is_dropped_because_i_only_want_hybrid_or_onsite_there() =>
        Assert.Equal("In Pune you want Hybrid or Onsite, this job is Remote",
            RuleFilter.Check(J("Senior .NET Developer", loc: "Pune", mode: WorkMode.Remote), PuneHybridElsewhereRemote()));

    [Fact] public void Onsite_or_hybrid_in_another_city_is_dropped_but_remote_passes()
    {
        var r = PuneHybridElsewhereRemote();
        Assert.StartsWith("Outside your listed places you want Remote", RuleFilter.Check(J("Senior .NET Developer", loc: "Bengaluru", mode: WorkMode.Onsite), r));
        Assert.StartsWith("Outside your listed places", RuleFilter.Check(J("Senior .NET Developer", loc: "Mumbai", mode: WorkMode.Hybrid), r));
        Assert.Null(RuleFilter.Check(J("Senior .NET Developer", loc: "Anywhere in the World", mode: WorkMode.Remote), r));
    }

    [Fact] public void Remote_job_restricted_to_another_region_is_still_dropped_by_the_region_check() =>
        Assert.StartsWith("Location restricted", RuleFilter.Check(J("Senior .NET Developer", loc: "USA Only", mode: WorkMode.Remote), PuneHybridElsewhereRemote()));

    [Fact] public void Each_place_has_its_own_modes()
    {
        var r = PuneHybridElsewhereRemote();
        r.LocationPrefs.Add(new LocationPref { Place = "Bengaluru", Modes = { "Remote", "Hybrid", "Onsite" } });
        Assert.Null(RuleFilter.Check(J("Senior .NET Developer", loc: "Bengaluru", mode: WorkMode.Onsite), r));
        Assert.NotNull(RuleFilter.Check(J("Senior .NET Developer", loc: "Pune", mode: WorkMode.Remote), r));
    }

    [Fact] public void A_place_with_no_modes_ticked_accepts_nothing_there()
    {
        var r = new Rule { LocationPrefs = { new LocationPref { Place = "Pune" } } };
        Assert.Equal("In Pune you want nothing, this job is Onsite", RuleFilter.Check(J("Any", loc: "Pune"), r));
    }

    [Fact] public void No_place_list_and_all_modes_means_no_location_preference() =>
        Assert.Null(RuleFilter.LocationModeReason(J("Any", loc: "Austin, TX"), new Rule()));

    [Fact] public void Only_remote_everywhere_with_no_places_listed_works_too()
    {
        var r = new Rule { OtherModes = new() { "Remote" } };
        Assert.NotNull(RuleFilter.LocationModeReason(J("Any", loc: "Pune", mode: WorkMode.Onsite), r));
        Assert.Null(RuleFilter.LocationModeReason(J("Any", loc: "Worldwide", mode: WorkMode.Remote), r));
    }

    [Fact] public void Too_much_required_experience_is_dropped() =>
        Assert.StartsWith("Needs 15+", RuleFilter.Check(J("Senior .NET Developer", desc: "You have 15+ years of experience in .NET"), MyRules()));

    [Fact] public void Required_years_reads_both_phrasings()
    {
        Assert.Equal(5, RuleFilter.RequiredYears(J("x", desc: "5+ years of professional experience")));
        Assert.Equal(3, RuleFilter.RequiredYears(J("x", desc: "Experience: at least 3 years in C#")));
        Assert.Null(RuleFilter.RequiredYears(J("x", desc: "We were founded 10 years ago.")));
    }

    [Fact] public void Salary_below_minimum_is_dropped()
    {
        var r = MyRules(); r.MinSalaryLpa = 25;
        Assert.StartsWith("Salary", RuleFilter.Check(J("Senior .NET Developer", desc: "Pay 10-15 LPA"), r));
    }

    [Fact] public void Alert_job_with_only_title_skips_location_and_mode_checks()
    {
        var alert = new Job { Title = "Senior .NET Developer", Company = "", Location = "", Description = "", WorkMode = WorkMode.Onsite };
        Assert.Null(RuleFilter.Check(alert, PuneHybridElsewhereRemote()));
    }

    [Fact] public void Empty_rules_let_everything_through() =>
        Assert.Null(RuleFilter.Check(J("Anything"), new Rule()));
}
