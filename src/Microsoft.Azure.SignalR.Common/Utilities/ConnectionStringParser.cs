// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Azure.Identity;

namespace Microsoft.Azure.SignalR
{
    internal static class ConnectionStringParser
    {
        private const string AccessKeyProperty = "accesskey";

        private const string AuthTypeProperty = "authtype";

        private const string ClientCertProperty = "clientCert";

        private const string ClientEndpointProperty = "clientEndpoint";

        private const string ClientIdProperty = "clientId";

        private const string ClientSecretProperty = "clientSecret";

        private const string EndpointProperty = "endpoint";

        private const string InvalidVersionValueFormat = "Version {0} is not supported.";

        private const string PortProperty = "port";

        private const string ServerEndpoint = "ServerEndpoint";

        // For SDK 1.x, only support Azure SignalR Service 1.x
        private const string SupportedVersion = "1";

        private const string TenantIdProperty = "tenantId";

        private const string TypeAzure = "azure";

        private const string TypeAzureAD = "aad";

        private const string TypeAzureApp = "azure.app";

        private const string TypeAzureMsi = "azure.msi";

        private const string ValidVersionRegex = "^" + SupportedVersion + @"\.\d+(?:[\w-.]+)?$";

        private const string VersionProperty = "version";

        private static readonly string InvalidClientEndpointProperty = $"Invalid value for {ClientEndpointProperty} property, it must be a valid URI.";

        private static readonly string InvalidEndpointProperty = $"Invalid value for {EndpointProperty} property, it must be a valid URI.";

        private static readonly string InvalidPortValue = $"Invalid value for {PortProperty} property, it must be an positive integer between (0, 65536)";

        private static readonly char[] KeyValueSeparator = { '=' };

        private static readonly string MissingAccessKeyProperty =
            $"{AccessKeyProperty} is required.";

        private static readonly string MissingClientIdProperty =
            $"Connection string missing required properties {ClientIdProperty}.";

        private static readonly string MissingClientSecretProperty =
            $"Connection string missing required properties {ClientSecretProperty} or {ClientCertProperty}.";

        private static readonly string MissingEndpointProperty =
                                            $"Connection string missing required properties {EndpointProperty}.";

        private static readonly string MissingTenantIdProperty =
                            $"Connection string missing required properties {TenantIdProperty}.";

        private static readonly char[] PropertySeparator = { ';' };

        internal static ParsedConnectionString Parse(string connectionString)
        {
            var dict = ToDictionary(connectionString);

            // parse and validate endpoint.
            if (!dict.TryGetValue(EndpointProperty, out var endpoint))
            {
                throw new ArgumentException(MissingEndpointProperty, nameof(endpoint));
            }
            endpoint = endpoint.TrimEnd('/');

            if (!TryGetEndpointUri(endpoint, out var endpointUri))
            {
                throw new ArgumentException(InvalidEndpointProperty, nameof(endpoint));
            }
            var builder = new UriBuilder(endpointUri);

            // parse and validate version.
            string version = null;
            if (dict.TryGetValue(VersionProperty, out var v))
            {
                if (!Regex.IsMatch(v, ValidVersionRegex))
                {
                    throw new ArgumentException(string.Format(InvalidVersionValueFormat, v), nameof(version));
                }
                version = v;
            }

            // parse and validate port.
            if (dict.TryGetValue(PortProperty, out var s))
            {
                if (int.TryParse(s, out var port) && port > 0 && port <= 0xFFFF)
                {
                    builder.Port = port;
                }
                else
                {
                    throw new ArgumentException(InvalidPortValue, nameof(port));
                }
            }

            Uri clientEndpointUri = null;

            // parse and validate clientEndpoint.
            if (dict.TryGetValue(ClientEndpointProperty, out var clientEndpoint))
            {
                if (!TryGetEndpointUri(clientEndpoint, out clientEndpointUri))
                {
                    throw new ArgumentException(InvalidClientEndpointProperty, nameof(clientEndpoint));
                }
            }

            // try building accesskey.
            dict.TryGetValue(AuthTypeProperty, out var type);
            var accessKey = type?.ToLower() switch
            {
                TypeAzureAD => BuildAadAccessKey(builder.Uri, dict),
                TypeAzure => BuildAzureAccessKey(builder.Uri, dict),
                TypeAzureApp => BuildAzureAppAccessKey(builder.Uri, dict),
                TypeAzureMsi => BuildAzureMsiAccessKey(builder.Uri, dict),
                _ => BuildAccessKey(builder.Uri, dict),
            };

            Uri serverEndpointUri = null;
            if (dict.TryGetValue(ServerEndpoint, out var serverEndpoint))
            {
                if (!TryGetEndpointUri(serverEndpoint, out serverEndpointUri))
                {
                    throw new ArgumentException($"{ServerEndpoint} property in connection string is not a valid URI: {serverEndpoint}.");
                }
            }
            return new ParsedConnectionString()
            {
                Endpoint = builder.Uri,
                ClientEndpoint = clientEndpointUri,
                AccessKey = accessKey,
                Version = version,
                ServerEndpoint = serverEndpointUri
            };
        }

        internal static bool TryGetEndpointUri(string endpoint, out Uri uriResult)
        {
            return Uri.TryCreate(endpoint, UriKind.Absolute, out uriResult) &&
                   (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        private static AccessKey BuildAadAccessKey(Uri uri, Dictionary<string, string> dict)
        {
            if (dict.TryGetValue(ClientIdProperty, out var clientId))
            {
                if (dict.TryGetValue(TenantIdProperty, out var tenantId))
                {
                    if (dict.TryGetValue(ClientSecretProperty, out var clientSecret))
                    {
                        return new AadAccessKey(uri, new ClientSecretCredential(tenantId, clientId, clientSecret));
                    }
                    else if (dict.TryGetValue(ClientCertProperty, out var clientCertPath))
                    {
                        return new AadAccessKey(uri, new ClientCertificateCredential(tenantId, clientId, clientCertPath));
                    }
                    else
                    {
                        throw new ArgumentException(MissingClientSecretProperty, ClientSecretProperty);
                    }
                }
                else
                {
                    return new AadAccessKey(uri, new ManagedIdentityCredential(clientId));
                }
            }
            else
            {
                return new AadAccessKey(uri, new ManagedIdentityCredential());
            }
        }

        private static AccessKey BuildAccessKey(Uri uri, Dictionary<string, string> dict)
        {
            if (dict.TryGetValue(AccessKeyProperty, out var key))
            {
                return new AccessKey(uri, key);
            }
            throw new ArgumentException(MissingAccessKeyProperty, AccessKeyProperty);
        }

        private static AccessKey BuildAzureAccessKey(Uri uri, Dictionary<string, string> dict)
        {
            return new AadAccessKey(uri, new DefaultAzureCredential());
        }

        private static AccessKey BuildAzureAppAccessKey(Uri uri, Dictionary<string, string> dict)
        {
            if (!dict.TryGetValue(ClientIdProperty, out var clientId))
            {
                throw new ArgumentException(MissingClientIdProperty, ClientIdProperty);
            }

            if (!dict.TryGetValue(TenantIdProperty, out var tenantId))
            {
                throw new ArgumentException(MissingTenantIdProperty, TenantIdProperty);
            }

            if (dict.TryGetValue(ClientSecretProperty, out var clientSecret))
            {
                return new AadAccessKey(uri, new ClientSecretCredential(tenantId, clientId, clientSecret));
            }
            else if (dict.TryGetValue(ClientCertProperty, out var clientCertPath))
            {
                return new AadAccessKey(uri, new ClientCertificateCredential(tenantId, clientId, clientCertPath));
            }
            throw new ArgumentException(MissingClientSecretProperty, ClientSecretProperty);
        }

        private static AccessKey BuildAzureMsiAccessKey(Uri uri, Dictionary<string, string> dict)
        {
            if (dict.TryGetValue(ClientIdProperty, out var clientId))
            {
                return new AadAccessKey(uri, new ManagedIdentityCredential(clientId));
            }
            return new AadAccessKey(uri, new ManagedIdentityCredential());
        }

        private static Dictionary<string, string> ToDictionary(string connectionString)
        {
            var properties = connectionString.Split(PropertySeparator, StringSplitOptions.RemoveEmptyEntries);
            if (properties.Length < 2)
            {
                throw new ArgumentException(MissingEndpointProperty, nameof(connectionString));
            }

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in properties)
            {
                var kvp = property.Split(KeyValueSeparator, 2);
                if (kvp.Length != 2)
                {
                    continue;
                }

                var key = kvp[0].Trim();
                if (dict.ContainsKey(key))
                {
                    throw new ArgumentException($"Duplicate properties found in connection string: {key}.");
                }

                dict.Add(key, kvp[1].Trim());
            }
            return dict;
        }
    }
}