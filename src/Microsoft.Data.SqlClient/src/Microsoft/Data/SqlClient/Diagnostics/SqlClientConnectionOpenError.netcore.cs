// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if NET

using System;
using System.Collections;
using System.Collections.Generic;

namespace Microsoft.Data.SqlClient.Diagnostics
{
    /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlClientDiagnostic.xml' path='docs/members[@name="SqlClientConnectionOpenError"]/SqlClientConnectionOpenError/*'/>
    public sealed class SqlClientConnectionOpenError : IReadOnlyList<KeyValuePair<string, object>>
    {
        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlClientDiagnostic.xml' path='docs/members[@name="SqlClientConnectionOpenError"]/Name/*'/>

        public const string Name = "Microsoft.Data.SqlClient.WriteConnectionOpenError";

        internal SqlClientConnectionOpenError(Guid operationId, string operation, long timestamp, Guid connectionId, SqlConnection connection, string clientVersion, Exception exception)
        {
            OperationId = operationId;
            Operation = operation;
            Timestamp = timestamp;
            ConnectionId = connectionId;
            Connection = connection;
            ClientVersion = clientVersion;
            Exception = exception;
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlClientDiagnostic.xml' path='docs/members[@name="SqlClientDiagnostic"]/OperationId/*'/>
        public Guid OperationId { get; }
        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlClientDiagnostic.xml' path='docs/members[@name="SqlClientDiagnostic"]/Operation/*'/>
        public string Operation { get; }
        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlClientDiagnostic.xml' path='docs/members[@name="SqlClientDiagnostic"]/Timestamp/*'/>
        public long Timestamp { get; }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlClientDiagnostic.xml' path='docs/members[@name="SqlClientConnectionOpenError"]/ConnectionId/*'/>
        public Guid ConnectionId { get; }
        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlClientDiagnostic.xml' path='docs/members[@name="SqlClientConnectionOpenError"]/Connection/*'/>
        public SqlConnection Connection { get; }
        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlClientDiagnostic.xml' path='docs/members[@name="SqlClientConnectionOpenError"]/ClientVersion/*'/>
        public string ClientVersion { get; }
        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlClientDiagnostic.xml' path='docs/members[@name="SqlClientConnectionOpenError"]/Exception/*'/>
        public Exception Exception { get; }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlClientDiagnostic.xml' path='docs/members[@name="SqlClientDiagnostic"]/Count/*'/>
        public int Count => 3 + 4;

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlClientDiagnostic.xml' path='docs/members[@name="SqlClientDiagnostic"]/Item1/*'/>
        public KeyValuePair<string, object> this[int index]
        {
            get => index switch
            {
                0 => new KeyValuePair<string, object>(nameof(OperationId), OperationId),
                1 => new KeyValuePair<string, object>(nameof(Operation), Operation),
                2 => new KeyValuePair<string, object>(nameof(Timestamp), Timestamp),
                3 => new KeyValuePair<string, object>(nameof(ConnectionId), ConnectionId),
                4 => new KeyValuePair<string, object>(nameof(Connection), Connection),
                5 => new KeyValuePair<string, object>(nameof(ClientVersion), ClientVersion),
                6 => new KeyValuePair<string, object>(nameof(Exception), Exception),
                _ => throw new IndexOutOfRangeException(nameof(index)),
            };
        }

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlClientDiagnostic.xml' path='docs/members[@name="SqlClientDiagnostic"]/GetEnumerator/*'/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <include file='../../../../../../../doc/snippets/Microsoft.Data.SqlClient/SqlClientDiagnostic.xml' path='docs/members[@name="SqlClientDiagnostic"]/GetEnumerator/*'/>
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            int count = Count;
            for (int index = 0; index < count; index++)
            {
                yield return this[index];
            }
        }
    }
}

#endif
