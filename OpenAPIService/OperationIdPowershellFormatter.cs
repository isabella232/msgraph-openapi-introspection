using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Services;
using System.Text;

namespace Microsoft.Graph.OpenAPIService
{
    /// <summary>
    /// The last '.' character of the OperationId value separates the method group from the operation name.
    /// This is replaced with an '_' to format the OperationId to allow for the creation of logical Powershell cmdlet names
    /// </summary>
    internal class OperationIdPowershellFormatter : OpenApiVisitorBase
    {
        public override void Visit(OpenApiPathItem pathItem)
        {
            var operationId = pathItem.Operations[OperationType.Get].OperationId; 

            int charPos = operationId.LastIndexOf('.', operationId.Length - 1);
            if (charPos >= 0)
            {
                StringBuilder newOperationId = new StringBuilder(operationId);

                newOperationId[charPos] = '_';
                pathItem.Operations[OperationType.Get].OperationId = newOperationId.ToString();
            }
        }
    }
}
