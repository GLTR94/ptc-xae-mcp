using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests.Fakes;

internal sealed partial class FakeTwinCatSession
{
    // ---- runtime-activation ----
    public LicenseInfoResult FakeLicenseInfoResult { get; set; } =
        new LicenseInfoResult(Array.Empty<LicenseEntry>(), "<LicenseInfo/>");
    public Exception? GetLicenseInfoThrows { get; set; }
    public string FakeDismissSecurityDialogResult { get; set; } = "Security dialog dismissed.";
    public Exception? DismissSecurityDialogThrows { get; set; }

    public RequiredLicensesResult FakeRequiredLicensesResult { get; set; } =
        new RequiredLicensesResult(Array.Empty<RequiredLicense>(), "<LicenseDef/>");
    public Exception? ListRequiredLicensesThrows { get; set; }

    public AddRequiredLicenseResult FakeAddRequiredLicenseResult { get; set; } =
        new AddRequiredLicenseResult(Added: true, WasAlreadyPresent: false,
            RequiredLicenses: Array.Empty<RequiredLicense>(), Message: "License added (fake).");
    public Exception? AddRequiredLicenseThrows { get; set; }

    public RemoveRequiredLicenseResult FakeRemoveRequiredLicenseResult { get; set; } =
        new RemoveRequiredLicenseResult(Removed: true, WasAbsent: false,
            RequiredLicenses: Array.Empty<RequiredLicense>(), Message: "License removed (fake).");
    public Exception? RemoveRequiredLicenseThrows { get; set; }

    public LicenseInfoResult GetLicenseInfo()
    {
        CallLog.Add("GetLicenseInfo()");
        if (GetLicenseInfoThrows is not null) throw GetLicenseInfoThrows;
        return FakeLicenseInfoResult;
    }

    public RequiredLicensesResult ListRequiredLicenses()
    {
        CallLog.Add("ListRequiredLicenses()");
        if (ListRequiredLicensesThrows is not null) throw ListRequiredLicensesThrows;
        return FakeRequiredLicensesResult;
    }

    public AddRequiredLicenseResult AddRequiredLicense(string? licenseId, string? orderNo)
    {
        CallLog.Add($"AddRequiredLicense({licenseId},{orderNo})");
        if (AddRequiredLicenseThrows is not null) throw AddRequiredLicenseThrows;
        return FakeAddRequiredLicenseResult;
    }

    public RemoveRequiredLicenseResult RemoveRequiredLicense(string? licenseId, string? orderNo)
    {
        CallLog.Add($"RemoveRequiredLicense({licenseId},{orderNo})");
        if (RemoveRequiredLicenseThrows is not null) throw RemoveRequiredLicenseThrows;
        return FakeRemoveRequiredLicenseResult;
    }

    public string DismissSecurityDialog()
    {
        CallLog.Add("DismissSecurityDialog()");
        if (DismissSecurityDialogThrows is not null) throw DismissSecurityDialogThrows;
        return FakeDismissSecurityDialogResult;
    }

    public string FakeOpenLicenseManagerResult { get; set; } =
        "License Manager opened and button clicked (fake).";
    public string OpenLicenseManagerAndRequestTrial()
    {
        CallLog.Add("OpenLicenseManagerAndRequestTrial()");
        return FakeOpenLicenseManagerResult;
    }

    public (string Message, byte[]? ScreenshotPng) FakeGenerateTrialLicensesResult { get; set; } =
        ("Trial licenses generated (fake).", null);
    public (string Message, byte[]? ScreenshotPng) GenerateTrialLicenses()
    {
        CallLog.Add("GenerateTrialLicenses()");
        return FakeGenerateTrialLicensesResult;
    }

    public string SubmitSecurityCode(string code)
    {
        CallLog.Add($"SubmitSecurityCode({code})");
        return $"Security code '{code}' entered (fake).";
    }
}
