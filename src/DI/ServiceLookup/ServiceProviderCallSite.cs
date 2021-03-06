// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using DI.ServiceLookup;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    internal class ServiceProviderCallSite : IServiceCallSite
    {
        public Type ServiceType { get; } = typeof(IServiceProvider);
        public Type ImplementationType { get; } = typeof(ServiceProvider);
        public CallSiteType CallSiteType => CallSiteType.ServiceProviderCallSite;
    }
}
