using Content.Shared.Humanoid;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Passports;

[Serializable, NetSerializable]
public enum ServiceCredentialUiKey : byte { Key }

[Serializable, NetSerializable]
public enum CredentialService : byte { Dvb, Fleet }

[RegisterComponent]
public sealed partial class ServiceCredentialComponent : Component
{
    [DataField] public CredentialService Service;
    [DataField] public string JobId = "";
    [DataField] public string JobName = "";
    [DataField] public string OwnerName = "";
    [DataField] public string Number = "";
    [DataField] public string PersonalNumber = "";
    [DataField] public string RegistrationNumber = "";
    [DataField] public string Species = "";
    [DataField] public string Sex = "";
    [DataField] public int Age;
    [DataField] public HumanoidCharacterAppearance? Portrait;
    [DataField] public string Uniform = "";
}

/// <summary>Prevents duplicate issuance if the same spawn event is repeated for a character.</summary>
[RegisterComponent]
public sealed partial class ServiceCredentialIssuedComponent : Component;

[Serializable, NetSerializable]
public sealed class ServiceCredentialUiState(CredentialService service, string jobName, string ownerName,
    string number, string species, string sex, int age, HumanoidCharacterAppearance? portrait,
    string uniform, string personalNumber = "", string registrationNumber = "") : BoundUserInterfaceState
{
    public readonly CredentialService Service = service;
    public readonly string JobName = jobName;
    public readonly string OwnerName = ownerName;
    public readonly string Number = number;
    public readonly string PersonalNumber = personalNumber;
    public readonly string RegistrationNumber = registrationNumber;
    public readonly string Species = species;
    public readonly string Sex = sex;
    public readonly int Age = age;
    public readonly HumanoidCharacterAppearance? Portrait = portrait;
    public readonly string Uniform = uniform;
}

public static class ServiceCredentialNumbers
{
    public static string Create(int digits)
    {
        var modulus = digits switch
        {
            6 => 1000000UL,
            8 => 100000000UL,
            12 => 1000000000000UL,
            _ => throw new ArgumentOutOfRangeException(nameof(digits))
        };
        // Parse explicitly: Convert.ToUInt64(string, int) is outside the client sandbox allowlist.
        var token = Guid.NewGuid().ToString("N");
        ulong value = 0;
        for (var i = 0; i < 15; i++)
        {
            var digit = token[i] is >= '0' and <= '9' ? token[i] - '0' : token[i] - 'a' + 10;
            value = value * 16 + (ulong) digit;
        }
        value %= modulus;
        return value.ToString($"D{digits}", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>Explicit issuance list: unrelated security, mercenary and civilian jobs receive no credential.</summary>
public static class ServiceCredentialJobs
{
    public static readonly IReadOnlyList<string> Dvb = new[]
    {
        "Sheriff", "Bailiff", "SeniorOfficer", "Deputy", "Cadet", "NFDetective", "PublicAffairsLiaison"
    };
    public static readonly IReadOnlyList<string> Fleet = new[]
    {
        "NavyKomandor", "NavyCaptain", "NavyOfficer", "NavyBrigmedic", "NavyCadet"
    };

    public static bool TryGet(string? job, out CredentialService service, out string prototype)
    {
        service = CredentialService.Dvb;
        prototype = string.Empty;
        if (job == null)
            return false;
        if (Contains(Dvb, job))
            prototype = $"RadiantCredentialDvb{job}";
        else if (Contains(Fleet, job))
        {
            service = CredentialService.Fleet;
            prototype = $"RadiantCredentialFleet{job}";
        }
        return prototype.Length > 0;
    }

    private static bool Contains(IReadOnlyList<string> jobs, string job)
    {
        foreach (var candidate in jobs)
            if (candidate == job)
                return true;
        return false;
    }
}
