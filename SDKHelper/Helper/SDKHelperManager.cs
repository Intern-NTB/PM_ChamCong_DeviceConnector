using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System;

namespace SDK.Helper
{
    public class SDKHelperManager
    {
        private static readonly ConcurrentDictionary<string, SDKHelper> _sdkHelpers = new ConcurrentDictionary<string, SDKHelper>();
        private readonly IServiceProvider _serviceProvider;

        public SDKHelperManager(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public SDKHelper GetOrCreateSDKHelper(string clientId)
        {
            return _sdkHelpers.GetOrAdd(clientId, _ =>
            {
                var scope = _serviceProvider.CreateScope();
                var sdkHelper = scope.ServiceProvider.GetRequiredService<SDKHelper>();
                return sdkHelper;
            });
        }

        public void RemoveSDKHelper(string clientId)
        {
            if (_sdkHelpers.TryRemove(clientId, out var sdkHelper))
            {
                sdkHelper.Dispose();
            }
        }

        public bool HasSDKHelper(string clientId)
        {
            return _sdkHelpers.ContainsKey(clientId);
        }
    }
} 