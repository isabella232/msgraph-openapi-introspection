using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph.OpenAPIService;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace apislice.Controllers
{
   

    /// <summary>
    /// Controller that enables querying over an OpenAPI document
    /// </summary>
    public class OpenApiController : ControllerBase
    {
        [Route("openapi")]
        [Route("{version}/openapi")]
        [Route("$openapi")]
        [Route("{version}/$openapi")]
        [HttpGet]
        public IActionResult Get(string version = "v1.0",
                                    [FromQuery]string operationIds = null,
                                    [FromQuery]string tags = null,
                                    [FromQuery]string openApiVersion = "2",
                                    [FromQuery]string title = "Partial Graph API",
                                    [FromQuery]OpenApiStyle style = OpenApiStyle.Plain,
                                    [FromQuery]string format = "yaml")
        {
            if (version != "v1.0" && version !="beta") return new NotFoundResult();

            var predicate = OpenApiService.CreatePredicate(operationIds, tags);

            if (predicate == null)
            {
                return new BadRequestResult();
            }

            var subsetOpenApiDocument = OpenApiService.CreateFilteredDocument(title, version, predicate);

            subsetOpenApiDocument = OpenApiService.ApplyStyle(style, subsetOpenApiDocument);

            var stream = OpenApiService.SerializeOpenApiDocument(subsetOpenApiDocument, openApiVersion, format);
            return new FileStreamResult(stream, "application/json");
        }
    }
}
