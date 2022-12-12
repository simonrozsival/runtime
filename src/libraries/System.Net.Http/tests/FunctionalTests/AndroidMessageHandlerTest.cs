// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Test.Common;
using System.Threading.Tasks;

using Xunit;
using Xunit.Abstractions;

namespace System.Net.Http.Functional.Tests
{
    public class AndroidMessageHandlerTest
    {
        [Fact]
        public async Task MakesGetRequest()
        {
            using var handler = new AndroidMessageHandler();
            using var client = new HttpClient(handler);

            var response = await client.GetAsync("https://dot.net/");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
