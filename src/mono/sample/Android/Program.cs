// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

var host = "self-signed.badssl.com";
var ipAddress = IPAddress.Parse("104.154.89.105");
// var host = "dns.google";
// var ipAddress = IPAddress.Parse("8.8.8.8");
var client = new TcpClient();

try
{
    Console.WriteLine($"Connecting TCP socket`...");
    await client.ConnectAsync(ipAddress, 443);

    var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false, ValidateServerCert);
    Console.WriteLine($"Authenticating TLS client...");
    await sslStream.AuthenticateAsClientAsync(host);

    Console.WriteLine("Connected, authenticated.");

    return 42;
}
catch (Exception ex)
{
    Console.WriteLine(ex);
    return 1;
}

static bool ValidateServerCert(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
{
    Console.WriteLine($"Validating server certificate: {sslPolicyErrors}");
    Console.WriteLine($"- certificate: {certificate}");
    Console.WriteLine($"- chain ({chain.ChainElements.Count} elements):");

    Console.WriteLine($"  - chain status:");
    foreach (var status in chain.ChainStatus)
    {
        Console.WriteLine($"    - status: {status.Status} {status.StatusInformation}");
    }

    foreach (var el in chain.ChainElements)
    {
        Console.WriteLine($"  - chain element cert:");
        Console.WriteLine($"    - status:");
        foreach (var status in el.ChainElementStatus)
            Console.WriteLine($"        - {status.Status} {status.StatusInformation}");
        Console.WriteLine($"    - cert: {el.Certificate}");
    }

    var chain2 = X509Chain.Create();
    var res = chain2.Build((X509Certificate2)certificate);
    Console.WriteLine($"- chain2.Build: {res}");

    Console.WriteLine($"  - chain status:");
    foreach (var status in chain2.ChainStatus)
    {
        Console.WriteLine($"    - status: {status.Status} {status.StatusInformation}");
    }

    foreach (var el in chain2.ChainElements)
    {
        Console.WriteLine($"  - chain2 element cert:");
        Console.WriteLine($"    - status:");
        foreach (var status in el.ChainElementStatus)
            Console.WriteLine($"        - {status.Status} {status.StatusInformation}");
        Console.WriteLine($"    - cert: {el.Certificate}");
    }

    return true;
}
