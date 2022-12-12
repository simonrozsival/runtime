// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Net.Http;

try
{
    var handler = new HttpClientHandler();
    var client = new HttpClient(handler);
    var result = await client.GetAsync("https://www.google.com");
    Console.WriteLine(result);
    Console.WriteLine("body:");
    Console.WriteLine(await result.Content.ReadAsStringAsync());
    return 42;
}
catch (Exception ex)
{
    Console.WriteLine(ex);
    return 1;
}
