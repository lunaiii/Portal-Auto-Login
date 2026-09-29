using PortalAutoLogin;

// Published RC4 test vector: key "Key", plaintext "Plaintext".
string actual = PortalClient.EncryptPassword("Plaintext", "Key");
if (actual != "bbf316e8d940af0ad3")
    throw new Exception($"RC4 vector mismatch: {actual}");
Console.WriteLine("RC4 vector: OK");

if (args.Contains("--live"))
{
    NetworkCheck check = await PortalClient.CheckNetworkAsync();
    Console.WriteLine($"Live network probe: {check.State} ({check.Detail})");
    if (check.State != NetworkState.Online)
        throw new Exception("This live check expected the current connection to be online.");
}
