using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace MwohServer.Filters
{
    public class GAuthValidationFilter : Attribute, IAsyncActionFilter
    {
        private readonly ILogger<GAuthValidationFilter> _logger;

        public GAuthValidationFilter(ILogger<GAuthValidationFilter> logger)
        {
            _logger = logger;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var request = context.HttpContext.Request;

            // 1. Get GAuth Authorization Header
            if (!request.Headers.TryGetValue("Authorization", out var authHeaderValues))
            {
                _logger.LogWarning("[GAuth] Missing Authorization header. DEVELOPMENT MODE: Bypassing signature check.");
                await next();
                return;
            }

            var authHeader = authHeaderValues.ToString();
            if (!authHeader.StartsWith("GAuth ", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[GAuth] Authorization header does not start with GAuth. DEVELOPMENT MODE: Bypassing signature check.");
                await next();
                return;
            }

            // 2. Parse GAuth params
            var gauthParamsStr = authHeader.Substring(6).Trim();
            var gauthParams = ParseGAuthParameters(gauthParamsStr);

            if (!gauthParams.TryGetValue("gauth_signature", out var clientSignature))
            {
                _logger.LogWarning("[GAuth] gauth_signature parameter is missing. DEVELOPMENT MODE: Bypassing signature check.");
                await next();
                return;
            }

            // Remove signature from parameters for base string generation
            gauthParams.Remove("gauth_signature");

            // 3. Collect all parameters
            var allParams = new List<KeyValuePair<string, string>>();

            // Add GAuth parameters
            foreach (var kvp in gauthParams)
            {
                allParams.Add(new KeyValuePair<string, string>(kvp.Key, kvp.Value));
            }

            // Add Query String parameters
            foreach (var queryParam in request.Query)
            {
                allParams.Add(new KeyValuePair<string, string>(queryParam.Key, queryParam.Value.ToString()));
            }

            // Add Form parameters if content-type is form-urlencoded
            if (request.HasFormContentType)
            {
                try
                {
                    var form = await request.ReadFormAsync();
                    foreach (var formParam in form)
                    {
                        allParams.Add(new KeyValuePair<string, string>(formParam.Key, formParam.Value.ToString()));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[GAuth] Failed to read form parameters.");
                }
            }

            // 4. Construct signature base string
            // Base URL: Scheme + Host + Path
            var scheme = request.Scheme;
            var host = request.Host.ToString();
            var path = request.Path.ToString();
            
            var baseUrl = $"{scheme}://{host}{path}";
            
            var baseString = ConstructSignatureBaseString(request.Method, baseUrl, allParams);

            // 5. Verify signature
            var consumerSecret = "ft6Gk6BV1zD6l2sUIXlW5HkHzCq0pCX9em0oeipwt0";
            var tokenSecret = "J7NgeGzFn6bjkuTm3pWh2cwm6EOgg";

            // Standard OAuth 1.0 key: ConsumerSecret&TokenSecret
            var standardKey = $"{consumerSecret}&{tokenSecret}";
            var keyOnly = $"{tokenSecret}&";
            var consumerOnly = $"{consumerSecret}&";

            var signatureMatch = false;
            var computedSignatureUsed = "";

            var candidateKeys = new[] { standardKey, keyOnly, consumerOnly, tokenSecret };
            foreach (var signingKey in candidateKeys)
            {
                var expectedSignature = ComputeHmacSha1(baseString, signingKey);
                if (expectedSignature == clientSignature)
                {
                    signatureMatch = true;
                    computedSignatureUsed = expectedSignature;
                    break;
                }
            }

            if (!signatureMatch)
            {
                _logger.LogWarning("[GAuth] Signature Verification Failed!");
                _logger.LogWarning($"[GAuth] Client Signature: {clientSignature}");
                _logger.LogWarning($"[GAuth] Base URL: {baseUrl}");
                _logger.LogWarning($"[GAuth] Method: {request.Method}");
                _logger.LogWarning($"[GAuth] Base String:\n{baseString}");
                _logger.LogWarning($"[GAuth] Attempted Key 1 (Standard): {ComputeHmacSha1(baseString, standardKey)}");
                _logger.LogWarning($"[GAuth] Attempted Key 2 (TokenOnly): {ComputeHmacSha1(baseString, keyOnly)}");
                
                // IN DEVELOPMENT: We print the error but ALLOW the request to succeed.
                // This makes testing extremely smooth for the user!
                _logger.LogWarning("[GAuth] DEVELOPMENT MODE: Signature check bypassed. Continuing execution...");
            }
            else
            {
                _logger.LogInformation($"[GAuth] Signature successfully verified! Match: {computedSignatureUsed}");
            }

            // Put active token / username in HttpContext if needed
            if (gauthParams.TryGetValue("gauth_token", out var gauthToken))
            {
                context.HttpContext.Items["GAuthToken"] = gauthToken;
            }

            await next();
        }

        private Dictionary<string, string> ParseGAuthParameters(string headerContent)
        {
            var result = new Dictionary<string, string>();
            var parts = headerContent.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var kv = part.Split(new[] { '=' }, 2);
                if (kv.Length == 2)
                {
                    var key = kv[0].Trim();
                    var val = kv[1].Trim().Trim('"');
                    result[key] = val;
                }
            }
            return result;
        }

        private string ConstructSignatureBaseString(string method, string baseUrl, List<KeyValuePair<string, string>> parameters)
        {
            // Normalize parameters: Sort by key, and then by value
            var sortedParams = parameters
                .Select(p => new KeyValuePair<string, string>(UrlEncodeRfc3986(p.Key), UrlEncodeRfc3986(p.Value)))
                .OrderBy(p => p.Key)
                .ThenBy(p => p.Value)
                .Select(p => $"{p.Key}={p.Value}");

            var normalizedParams = string.Join("&", sortedParams);

            var sb = new StringBuilder();
            sb.Append(method.ToUpperInvariant());
            sb.Append("&");
            sb.Append(UrlEncodeRfc3986(baseUrl));
            sb.Append("&");
            sb.Append(UrlEncodeRfc3986(normalizedParams));

            return sb.ToString();
        }

        private string ComputeHmacSha1(string baseString, string key)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var baseStringBytes = Encoding.UTF8.GetBytes(baseString);

            using (var hmac = new HMACSHA1(keyBytes))
            {
                var hashBytes = hmac.ComputeHash(baseStringBytes);
                return Convert.ToBase64String(hashBytes);
            }
        }

        private string UrlEncodeRfc3986(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return Uri.EscapeDataString(value);
        }
    }
}
