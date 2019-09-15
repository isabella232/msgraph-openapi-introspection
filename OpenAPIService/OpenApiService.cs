using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Services;
using Microsoft.OpenApi.Validations;
using Microsoft.OpenApi.Writers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Microsoft.Graph.OpenAPIService
{
    public enum OpenApiStyle
    {
        PowerShell,
        PowerPlatform,
        Plain
    }

    public class OpenApiService
    {
        const string graphV1OpenApiUrl = "https://raw.githubusercontent.com/microsoftgraph/msgraph-metadata/master/openapi/v1.0/openapi.yaml";
        const string graphBetaOpenApiUrl = "https://raw.githubusercontent.com/microsoftgraph/msgraph-metadata/master/openapi/beta/openapi.yaml";
        static OpenApiDocument _OpenApiV1Document;
        static OpenApiDocument _OpenApiBetaDocument;

        public static OpenApiDocument CreateFilteredDocument(string title, string version, Func<OpenApiOperation, bool> predicate)
        {
            OpenApiDocument source = null;
            switch (version)
            {
                case "v1.0":
                    source = OpenApiService.GetGraphOpenApiV1();
                    break;
                case "beta":
                    source = OpenApiService.GetGraphOpenApiBeta();
                    break;
            }

            var subset = new OpenApiDocument
            {
                Info = new OpenApiInfo()
                {
                    Title = title,
                    Version = version
                },

                Components = new OpenApiComponents()
            };
            var aadv2Scheme = new OpenApiSecurityScheme()
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows()
                {
                    AuthorizationCode = new OpenApiOAuthFlow()
                    {
                        AuthorizationUrl = new Uri("https://login.microsoftonline.com/common/oauth2/v2.0/authorize"),
                        TokenUrl = new Uri("https://login.microsoftonline.com/common/oauth2/v2.0/token")
                    }
                },
                Reference = new OpenApiReference() { Id = "azureaadv2", Type = ReferenceType.SecurityScheme },
                UnresolvedReference = false
            };
            subset.Components.SecuritySchemes.Add("azureaadv2", aadv2Scheme);

            subset.SecurityRequirements.Add(new OpenApiSecurityRequirement() { { aadv2Scheme, new string[] { } } });
            
            subset.Servers.Add(new OpenApiServer() { Description = "Core", Url = "https://graph.microsoft.com/v1.0/" });

            var operationObjects = new List<OpenApiOperation>();
            var results = FindOperations(source, predicate);
            foreach (var result in results)
            {
                OpenApiPathItem pathItem = null;
                if (subset.Paths == null)
                {
                    subset.Paths = new OpenApiPaths();
                    pathItem = new OpenApiPathItem();
                    subset.Paths.Add(result.CurrentKeys.Path, pathItem);
                }
                else
                {
                    if (!subset.Paths.TryGetValue(result.CurrentKeys.Path, out pathItem))
                    {
                        pathItem = new OpenApiPathItem();
                        subset.Paths.Add(result.CurrentKeys.Path, pathItem);
                    }
                }

                pathItem.Operations.Add((OperationType)result.CurrentKeys.Operation, result.Operation);
            }

            OpenApiService.CopyReferences(subset);

            return subset;
        }

        public static Func<OpenApiOperation, bool> CreatePredicate(string operationIds, string tags)
        {
            if (operationIds != null && tags != null)
            {                
                return null; // Cannot filter by OperationIds and Tags at the same time
            }

            Func<OpenApiOperation, bool> predicate;
            if (operationIds != null)
            {
                if (operationIds == "*")
                {
                    predicate = (o) => true;  // All operations
                }
                else
                {
                    var operationIdsArray = operationIds.Split(',');
                    predicate = (o) => operationIdsArray.Contains(o.OperationId);
                }
            }
            else if (tags != null)
            {
                var tagsArray = tags.Split(',');
                if (tagsArray.Length == 1 && tagsArray[0].EndsWith("*"))
                {
                    var pattern = tagsArray[0].Substring(0, tagsArray[0].Length-1);
                    predicate = (o) => o.Tags.Any(t => t.Name.StartsWith(pattern));
                } else
                {
                    predicate = (o) => o.Tags.Any(t => tagsArray.Contains(t.Name));
                }
            }
            else
            {
                predicate = null;
            }

            return predicate;
        }

        public static MemoryStream SerializeOpenApiDocument(OpenApiDocument subset, string openApiVersion, string format)
        {
            var stream = new MemoryStream();
            var sr = new StreamWriter(stream);
            OpenApiWriterBase writer;
            if (format == "yaml")
            {
                writer = new OpenApiYamlWriter(sr);
            }
            else
            {
                writer = new OpenApiJsonWriter(sr);
            }

            if (openApiVersion == "2")
            {
                subset.SerializeAsV2(writer);
            }
            else
            {
                subset.SerializeAsV3(writer);
            }
            sr.Flush();
            stream.Position = 0;
            return stream;
        }

        public static OpenApiDocument GetGraphOpenApiV1()
        {
            if (_OpenApiV1Document != null)
            {
                return _OpenApiV1Document;
            }

            _OpenApiV1Document = GetOpenApiDocument(graphV1OpenApiUrl);

            return _OpenApiV1Document;
        }

        public static OpenApiDocument GetGraphOpenApiBeta()
        {
            if (_OpenApiBetaDocument != null)
            {
                return _OpenApiBetaDocument;
            }

            _OpenApiBetaDocument = GetOpenApiDocument(graphBetaOpenApiUrl);

            return _OpenApiBetaDocument;
        }

        public static OpenApiDocument ApplyStyle(OpenApiStyle style, OpenApiDocument subsetOpenApiDocument)
        {
            if (style == OpenApiStyle.Plain)
            {
                return subsetOpenApiDocument;
            }

            /* For Powershell and PowerPlatform Styles */

            // Clone doc before making changes
            subsetOpenApiDocument = Clone(subsetOpenApiDocument);

            var anyOfRemover = new AnyOfRemover();
            var walker = new OpenApiWalker(anyOfRemover);
            walker.Walk(subsetOpenApiDocument);
                        
            if (style == OpenApiStyle.PowerShell)
            {
                // Format the OperationId for Powershell cmdlet names generation 
                var operationIdFormatter = new OperationIdPowershellFormatter();
                walker = new OpenApiWalker(operationIdFormatter);
                walker.Walk(subsetOpenApiDocument);                
            }
                        
            return subsetOpenApiDocument;
        }

        private static OpenApiDocument Clone(OpenApiDocument subsetOpenApiDocument)
        {
            var stream = new MemoryStream();
            var writer = new OpenApiYamlWriter(new StreamWriter(stream));
            subsetOpenApiDocument.SerializeAsV3(writer);
            writer.Flush();
            stream.Position = 0;
            var reader = new OpenApiStreamReader();
            return reader.Read(stream, out OpenApiDiagnostic diag);
        }

        private static OpenApiDocument GetOpenApiDocument(string url)
        {
            HttpClient httpClient = CreateHttpClient();

            var response = httpClient.GetAsync(url)
                                .GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception("Failed to retrieve OpenApi document");
            }

            var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();

            var newrules = ValidationRuleSet.GetDefaultRuleSet().Rules
                .Where(r => r.GetType() != typeof(ValidationRule<OpenApiSchema>)).ToList();
            

            var reader = new OpenApiStreamReader(new OpenApiReaderSettings() {
                RuleSet = new ValidationRuleSet(newrules)
            });
            var openApiDoc = reader.Read(stream, out var diagnostic);

            if (diagnostic.Errors.Count > 0)
            {
                throw new Exception("OpenApi document has errors : " + String.Join("\n", diagnostic.Errors));
            }
            return openApiDoc;
        }

        private static IList<SearchResult> FindOperations(OpenApiDocument graphOpenApi, Func<OpenApiOperation, bool> predicate)
        {
            var search = new OperationSearch(predicate);
            var walker = new OpenApiWalker(search);
            walker.Walk(graphOpenApi);
            return search.SearchResults;
        }

        private static HttpClient CreateHttpClient()
        {
            System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var httpClient = new HttpClient(new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip
            });
            httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue("gzip"));
            httpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("apislice", "1.0"));
            return httpClient;
        }

        private static void CopyReferences(OpenApiDocument target)
        {
            bool morestuff = false;
            do
            {
                var copy = new CopyReferences(target);
                var walker = new OpenApiWalker(copy);
                walker.Walk(target);

                morestuff = Add(copy.Components, target.Components);
                
            } while (morestuff);
        }

        private static bool Add(OpenApiComponents newComponents, OpenApiComponents target)
        {
            var moreStuff = false; 
            foreach (var item in newComponents.Schemas)
            {
                if (!target.Schemas.ContainsKey(item.Key))
                {
                    moreStuff = true;
                    target.Schemas.Add(item);

                }
            }

            foreach (var item in newComponents.Parameters)
            {
                if (!target.Parameters.ContainsKey(item.Key))
                {
                    moreStuff = true;
                    target.Parameters.Add(item);
                }
            }

            foreach (var item in newComponents.Responses)
            {
                if (!target.Responses.ContainsKey(item.Key))
                {
                    moreStuff = true;
                    target.Responses.Add(item);
                }
            }
            return moreStuff;
        }
   }
}
