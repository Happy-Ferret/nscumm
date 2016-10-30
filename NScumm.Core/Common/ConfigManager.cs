﻿//
//  ConfigManager.cs
//
//  Author:
//       scemino <scemino74@gmail.com>
//
//  Copyright (c) 2016 scemino
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using static NScumm.Core.DebugHelper;

namespace NScumm.Core
{
    public static class ConfigManagerExtension
    {
        public static void Set<T>(this ConfigManager confManager, string key, T value)
        {
            confManager.Set(key, value);
        }

        public static T Get<T>(this ConfigManager confManager, string key, string domName = null)
        {
            return (T) confManager.Get(key, domName);
        }
    }

    public sealed class ConfigManager
    {
        private const string ApplicationDomain = "nscumm";
        private const string TransientDomain = "__TRANSIENT";
        public static readonly ConfigManager Instance = new ConfigManager();

        private readonly Dictionary<string, object> _transientDomain;
        private readonly Dictionary<string, object> _appDomain;
        private readonly Dictionary<string, object> _defaultsDomain;
        private readonly Dictionary<string, Dictionary<string, object>> _gameDomains =
            new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Dictionary<string, object>> _miscDomains =
            new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, object> _activeDomain;
        private string _activeDomainName;

        public string ActiveDomainName => _activeDomainName;

        private ConfigManager()
        {
            _transientDomain = new Dictionary<string, object>();
            _appDomain = new Dictionary<string, object>();
            _defaultsDomain = new Dictionary<string, object>();
        }

        public void RegisterDefault(string key, object value)
        {
            _defaultsDomain[key] = value;
        }

        public bool HasKey(string key)
        {
            // Search the domains in the following order:
            // 1) the transient domain,
            // 2) the active game domain (if any),
            // 3) the application domain.
            // The defaults domain is explicitly *not* checked.

            if (_transientDomain.ContainsKey(key))
                return true;

            if (_activeDomain != null && _activeDomain.ContainsKey(key))
                return true;

            if (_appDomain.ContainsKey(key))
                return true;

            return false;
        }

        public object Get(string key)
        {
            if (_transientDomain.ContainsKey(key))
                return _transientDomain[key];
            if (_activeDomain != null && _activeDomain.ContainsKey(key))
                return _activeDomain[key];
            if (_appDomain.ContainsKey(key))
                return _appDomain[key];

            return _defaultsDomain[key];
        }

        public object Get(string key, string domName)
        {
            // FIXME: For now we continue to allow empty domName to indicate
            // "use 'default' domain". This is mainly needed for the SCUMM ConfigDialog
            // and should be removed ASAP.
            if (string.IsNullOrEmpty(domName))
                return Get(key);

            var domain = GetDomain(domName);

            if (domain == null)
                Error("ConfigManager.Get({0},{1}) called on non-existent domain", key, domName);

            if (domain.ContainsKey(key))
                return domain[key];

            return _defaultsDomain[key];
        }

        public void Set(string key, object value)
        {
            // Remove the transient domain value, if any.
            _transientDomain.Remove(key);

            // Write the new key/value pair into the active domain, resp. into
            // the application domain if no game domain is active.
            if (_activeDomain != null)
                _activeDomain[key] = value;
            else
                _appDomain[key] = value;
        }

        private Dictionary<string, object> GetDomain(string domName)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrEmpty(domName));
            System.Diagnostics.Debug.Assert(IsValidDomainName(domName));

            if (domName == TransientDomain)
                return _transientDomain;
            if (domName == ApplicationDomain)
                return _appDomain;
#if ENABLE_KEYMAPPER
            if (domName == KeymapperDomain)
                return _keymapperDomain;
#endif
            if (_gameDomains.ContainsKey(domName))
                return _gameDomains[domName];
            if (_miscDomains.ContainsKey(domName))
                return _miscDomains[domName];

            return null;
        }

        private static bool IsValidDomainName(string domName)
        {
            var p = domName;
            var i = 0;
            while (i < p.Length && (char.IsLetterOrDigit(p[i]) || p[i] == '-' || p[i] == '_'))
                i++;
            return i == p.Length;
        }
    }
}