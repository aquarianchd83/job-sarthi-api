using System.Text.RegularExpressions;

namespace JobSarthi.Api.Resume;

/// <summary>Known tech skills (display name, lowercase search term). Used to read resumes, read job posts and guard tailored text.</summary>
public static class SkillCatalog
{
    public static readonly (string Name, string Term)[] All =
    {
        ("C#","c#"),(".NET",".net"),("ASP.NET Core","asp.net core"),("ASP.NET","asp.net"),("Entity Framework","entity framework"),("EF Core","ef core"),
        ("LINQ","linq"),("Web API","web api"),("REST APIs","rest"),("GraphQL","graphql"),("gRPC","grpc"),("SignalR","signalr"),("Blazor","blazor"),
        ("SQL Server","sql server"),("T-SQL","t-sql"),("PostgreSQL","postgresql"),("MySQL","mysql"),("MongoDB","mongodb"),("Redis","redis"),("Dapper","dapper"),
        ("Angular","angular"),("TypeScript","typescript"),("JavaScript","javascript"),("React","react"),("Vue","vue"),("RxJS","rxjs"),("NgRx","ngrx"),
        ("HTML","html"),("CSS","css"),("SCSS","scss"),("Node.js","node.js"),("Python","python"),("Java","java"),("Go","golang"),
        ("Azure","azure"),("AWS","aws"),("Docker","docker"),("Kubernetes","kubernetes"),("Terraform","terraform"),("CI/CD","ci/cd"),
        ("Azure DevOps","azure devops"),("GitHub Actions","github actions"),("Jenkins","jenkins"),("Git","git"),
        ("Microservices","microservices"),("RabbitMQ","rabbitmq"),("Kafka","kafka"),("Azure Service Bus","service bus"),
        ("xUnit","xunit"),("NUnit","nunit"),("Playwright","playwright"),("Selenium","selenium"),("Cypress","cypress"),("Jest","jest"),
        ("Unit Testing","unit test"),("TDD","tdd"),("Agile","agile"),("Scrum","scrum"),("Design Patterns","design patterns"),("OOP","oop"),
        ("SOLID","solid"),("Clean Architecture","clean architecture"),("DDD","domain-driven"),("OAuth","oauth"),("JWT","jwt"),("IdentityServer","identityserver"),
        ("Elasticsearch","elasticsearch"),("Power BI","power bi"),("SSIS","ssis"),("SSRS","ssrs"),("WPF","wpf"),("WinForms","winforms"),("Xamarin","xamarin"),("MAUI","maui"),
    };

    private static readonly Dictionary<string, Regex> Cache = new();

    private static Regex For(string term)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(term, out var rx))
                Cache[term] = rx = new Regex(@"(?<![a-z0-9#+.])" + Regex.Escape(term) + @"(?![a-z0-9#+])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
            return rx;
        }
    }

    /// <summary>How many times a term appears as a whole word in already-lowercased text.</summary>
    public static int Count(string lowerText, string term) => For(term.ToLowerInvariant()).Matches(lowerText).Count;

    /// <summary>The search term for a skill name: the catalog term when known, else the name itself.</summary>
    public static string TermFor(string skillName)
    {
        var hit = All.FirstOrDefault(s => s.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase));
        return hit.Term ?? skillName.ToLowerInvariant();
    }

    /// <summary>Catalog skills mentioned in the text, in catalog order.</summary>
    public static List<string> Find(string text)
    {
        var lower = text.ToLowerInvariant();
        return All.Where(s => Count(lower, s.Term) > 0).Select(s => s.Name).ToList();
    }
}
