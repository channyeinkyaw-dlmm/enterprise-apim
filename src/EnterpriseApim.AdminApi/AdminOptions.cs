namespace EnterpriseApim.AdminApi;

public sealed class AdminOptions
{
    public const string Section = "Admin";

    /// <summary>
    /// First admin account, created only when the database has no admin users.
    /// It must choose a new password at first sign-in; remove these settings afterwards.
    /// </summary>
    public string BootstrapUsername { get; set; } = "";
    public string BootstrapPassword { get; set; } = "";

    /// <summary>A session ends after this many minutes without a request.</summary>
    public int SessionIdleMinutes { get; set; } = 60;

    /// <summary>A session ends this many hours after sign-in, however active it is.</summary>
    public int SessionMaxHours { get; set; } = 12;

    /// <summary>Failed sign-ins in a row before the account is locked.</summary>
    public int MaxFailedSignIns { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}
