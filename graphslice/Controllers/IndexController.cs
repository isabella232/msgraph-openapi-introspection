using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Graph.OpenAPIService;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Services;

namespace apislice.Controllers
{
    [Route("list")]
    [ApiController]
    public class IndexController : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> Get(string graphVersion = "v1.0", bool forceRefresh = false)
        {
            var graphOpenApi = await OpenApiService.GetGraphOpenApiDocument(graphVersion,forceRefresh);
            WriteIndex(this.Request.Scheme + "://"+ this.Request.Host.Value,graphOpenApi, Response.Body);
            
            return new EmptyResult();
        }


        private static void WriteIndex(string baseUrl, OpenApiDocument graphOpenApi, Stream stream)
        {
            var sw = new StreamWriter(stream);
            
            var indexSearch = new OpenApiOperationIndex();
            var walker = new OpenApiWalker(indexSearch);

            walker.Walk(graphOpenApi);

            sw.AutoFlush = true;

            sw.WriteLine("<h1># OpenAPI Operations for Microsoft Graph</h1>");
            sw.WriteLine("<b/>");
            sw.WriteLine("<ul>");
            foreach (var item in indexSearch.Index)
            {
                
                var target = $"{baseUrl}/openapi?tags={item.Key.Name}&openApiVersion=3";
                sw.WriteLine($"<li><a href='./openapi?tags={target}'>{item.Key.Name}</a>   [<a href='/swagger/index.html#{target}'>Swagger UI</a>]</li>");
                sw.WriteLine("<ul>");
                foreach (var op in item.Value)
                {
                    sw.WriteLine("<li><a href='./openapi?operationIds=" + op.OperationId + "'>" + op.OperationId + "</a></li>");
                }
                sw.WriteLine("</ul>");
            }
            sw.WriteLine("</ul>");

        }
    }
}
