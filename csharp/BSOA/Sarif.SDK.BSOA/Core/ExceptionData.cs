// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;

namespace Microsoft.CodeAnalysis.Sarif
{
    /// <summary>
    /// Describes a condition relevant to the tool itself, as opposed to being relevant to a file being analyzed by the tool.
    /// </summary>
    public partial class ExceptionData
    {
        public static ExceptionData Create(Exception exception)
        {
            return new ExceptionData
            {
                Kind = exception.GetType().Name,
                Message = exception.Message,
                InnerExceptions = GetInnerExceptions(exception),
                Stack = Stack.Create(exception.StackTrace)
            };
        }

        private static IList<ExceptionData> GetInnerExceptions(Exception exception)
        {
            var innerExceptions = new List<ExceptionData>();

            IReadOnlyCollection<Exception> aggregateInnerExceptions = (exception as AggregateException)?.InnerExceptions;
            if (aggregateInnerExceptions != null)
            {
                foreach (Exception innerException in aggregateInnerExceptions)
                {
                    innerExceptions.Add(Create(innerException));
                }
            }
            else if (exception.InnerException != null)
            {
                innerExceptions.Add(Create(exception.InnerException));
            }

            return innerExceptions;
        }
    }
}
