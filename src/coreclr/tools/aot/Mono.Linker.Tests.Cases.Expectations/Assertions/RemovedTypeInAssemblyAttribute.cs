﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Mono.Linker.Tests.Cases.Expectations.Assertions
{
	[AttributeUsage (AttributeTargets.Class | AttributeTargets.Delegate, AllowMultiple = true, Inherited = false)]
	public class RemovedTypeInAssemblyAttribute : BaseInAssemblyAttribute
	{
		public RemovedTypeInAssemblyAttribute (string assemblyFileName, Type type)
		{
			ArgumentNullException.ThrowIfNull (type);
			ArgumentException.ThrowIfNullOrEmpty (assemblyFileName);
		}

		public RemovedTypeInAssemblyAttribute (string assemblyFileName, string typeName)
		{
			ArgumentException.ThrowIfNullOrEmpty (assemblyFileName);
			ArgumentException.ThrowIfNullOrEmpty (typeName);
		}
	}
}
