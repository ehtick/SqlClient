// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.Data.Common;
using Microsoft.Data.Common.ConnectionString;
using Microsoft.Data.ProviderBase;

namespace Microsoft.Data.SqlClient.ConnectionPool
{
    // set_ConnectionString calls DbConnectionFactory.GetConnectionPoolGroup
    // when not found a new pool entry is created and potentially added
    // DbConnectionPoolGroup starts in the Active state

    // Open calls DbConnectionFactory.GetConnectionPool
    // if the existing pool entry is Disabled, GetConnectionPoolGroup is called for a new entry
    // DbConnectionFactory.GetConnectionPool calls DbConnectionPoolGroup.GetConnectionPool

    // DbConnectionPoolGroup.GetConnectionPool will return pool for the current identity
    // or null if identity is restricted or pooling is disabled or state is disabled at time of add
    // state changes are Active->Active, Idle->Active

    // DbConnectionFactory.PruneConnectionPoolGroups calls Prune
    // which will QueuePoolForRelease on all empty pools
    // and once no pools remain, change state from Active->Idle->Disabled
    // Once Disabled, factory can remove its reference to the pool entry

    sealed internal class DbConnectionPoolGroup
    {
        private readonly DbConnectionOptions _connectionOptions;
        private readonly DbConnectionPoolKey _poolKey;
        private readonly DbConnectionPoolGroupOptions _poolGroupOptions;
        private ConcurrentDictionary<DbConnectionPoolIdentity, IDbConnectionPool> _poolCollection;

        private int _state;          // see PoolGroupState* below

        private DbConnectionPoolGroupProviderInfo _providerInfo;
        private DbMetaDataFactory _metaDataFactory;

        private static int s_objectTypeCount; // EventSource counter

        // always lock this before changing _state, we don't want to move out of the 'Disabled' state
        // PoolGroupStateUninitialized = 0;
        private const int PoolGroupStateActive = 1; // initial state, GetPoolGroup from cache, connection Open
        private const int PoolGroupStateIdle = 2; // all pools are pruned via Clear
        private const int PoolGroupStateDisabled = 4; // factory pool entry pruning method

        internal DbConnectionPoolGroup(DbConnectionOptions connectionOptions, DbConnectionPoolKey key, DbConnectionPoolGroupOptions poolGroupOptions)
        {
            Debug.Assert(connectionOptions != null, "null connection options");
#if NETFRAMEWORK
            Debug.Assert(poolGroupOptions == null || ADP.s_isWindowsNT, "should not have pooling options on Win9x");
#endif

            _connectionOptions = connectionOptions;
            _poolKey = key;
            _poolGroupOptions = poolGroupOptions;

            // always lock this object before changing state
            // HybridDictionary does not create any sub-objects until add
            // so it is safe to use for non-pooled connection as long as
            // we check _poolGroupOptions first
            _poolCollection = new ConcurrentDictionary<DbConnectionPoolIdentity, IDbConnectionPool>();
            _state = PoolGroupStateActive;
        }

        internal DbConnectionOptions ConnectionOptions => _connectionOptions;

        internal DbConnectionPoolKey PoolKey => _poolKey;

        internal DbConnectionPoolGroupProviderInfo ProviderInfo
        {
            get
            {
                return _providerInfo;
            }
            set
            {
                _providerInfo = value;
                if (value != null)
                {
                    _providerInfo.PoolGroup = this;
                }
            }
        }

        internal bool IsDisabled => (PoolGroupStateDisabled == _state);

        internal int ObjectID { get; } = Interlocked.Increment(ref s_objectTypeCount);

        internal DbConnectionPoolGroupOptions PoolGroupOptions => _poolGroupOptions;

        internal DbMetaDataFactory MetaDataFactory
        {
            get
            {
                return _metaDataFactory;
            }

            set
            {
                _metaDataFactory = value;
            }
        }

        internal int Clear()
        {
            // must be multi-thread safe with competing calls by Clear and Prune via background thread
            // will return the number of connections in the group after clearing has finished

            // First, note the old collection and create a new collection to be used
            ConcurrentDictionary<DbConnectionPoolIdentity, IDbConnectionPool> oldPoolCollection = null;
            lock (this)
            {
                if (_poolCollection.Count > 0)
                {
                    oldPoolCollection = _poolCollection;
                    _poolCollection = new ConcurrentDictionary<DbConnectionPoolIdentity, IDbConnectionPool>();
                }
            }

            // Then, if a new collection was created, release the pools from the old collection
            if (oldPoolCollection != null)
            {
                foreach (KeyValuePair<DbConnectionPoolIdentity, IDbConnectionPool> entry in oldPoolCollection)
                {
                    IDbConnectionPool pool = entry.Value;
                    if (pool != null)
                    {
                        SqlConnectionFactory connectionFactory = pool.ConnectionFactory;

                        connectionFactory.QueuePoolForRelease(pool, true);
                    }
                }
            }

            // Finally, return the pool collection count - this may be non-zero if something was added while we were clearing
            return _poolCollection.Count;
        }

        internal IDbConnectionPool GetConnectionPool(SqlConnectionFactory connectionFactory)
        {
            // When this method returns null it indicates that the connection
            // factory should not use pooling.

            // We don't support connection pooling on Win9x;
            // PoolGroupOptions will only be null when we're not supposed to pool
            // connections.
            IDbConnectionPool pool = null;
            if (_poolGroupOptions != null)
            {
#if NETFRAMEWORK
                Debug.Assert(ADP.s_isWindowsNT, "should not be pooling on Win9x");
#endif

                DbConnectionPoolIdentity currentIdentity = DbConnectionPoolIdentity.NoIdentity;

                if (_poolGroupOptions.PoolByIdentity)
                {
                    // if we're pooling by identity (because integrated security is
                    // being used for these connections) then we need to go out and
                    // search for the connectionPool that matches the current identity.

                    currentIdentity = DbConnectionPoolIdentity.GetCurrent();

                    // If the current token is restricted in some way, then we must
                    // not attempt to pool these connections.
                    if (currentIdentity.IsRestricted)
                    {
                        currentIdentity = null;
                    }
                }

                if (currentIdentity != null)
                {
                    if (!_poolCollection.TryGetValue(currentIdentity, out pool)) // find the pool
                    {
                        lock (this)
                        {
                            // Did someone already add it to the list?
                            if (!_poolCollection.TryGetValue(currentIdentity, out pool))
                            {
                                DbConnectionPoolProviderInfo connectionPoolProviderInfo = connectionFactory.CreateConnectionPoolProviderInfo(ConnectionOptions);

                                IDbConnectionPool newPool;
                                if (LocalAppContextSwitches.UseConnectionPoolV2)
                                {
                                    throw new NotImplementedException();
                                }
                                else
                                {
                                    // WaitHandleDbConnectionPool is the v1 pool implementation, and used by default if UseConnectionPoolV2 is off
                                    newPool = new WaitHandleDbConnectionPool(connectionFactory, this, currentIdentity, connectionPoolProviderInfo);
                                }

                                if (MarkPoolGroupAsActive())
                                {
                                    // If we get here, we know for certain that we there isn't
                                    // a pool that matches the current identity, so we have to
                                    // add the optimistically created one
                                    newPool.Startup(); // must start pool before usage
                                    bool addResult = _poolCollection.TryAdd(currentIdentity, newPool);
                                    Debug.Assert(addResult, "No other pool with current identity should exist at this point");
                                    SqlClientEventSource.Metrics.EnterActiveConnectionPool();

                                    pool = newPool;
                                }
                                else
                                {
                                    // else pool entry has been disabled so don't create new pools
                                    Debug.Assert(PoolGroupStateDisabled == _state, "state should be disabled");

                                    // don't need to call connectionFactory.QueuePoolForRelease(newPool) because
                                    // pool callbacks were delayed and no risk of connections being created
                                    newPool.Shutdown();
                                }
                            }
                            else
                            {
                                // else found an existing pool to use instead
                                Debug.Assert(PoolGroupStateActive == _state, "state should be active since a pool exists and lock holds");
                            }
                        }
                    }
                    // the found pool could be in any state
                }
            }

            if (pool == null)
            {
                lock (this)
                {
                    // keep the pool entry state active when not pooling
                    MarkPoolGroupAsActive();
                }
            }
            return pool;
        }

        private bool MarkPoolGroupAsActive()
        {
            // when getting a connection, make the entry active if it was idle (but not disabled)
            // must always lock this before calling

            if (PoolGroupStateIdle == _state)
            {
                _state = PoolGroupStateActive;
                SqlClientEventSource.Log.TryTraceEvent("<prov.DbConnectionPoolGroup.ClearInternal|RES|INFO|CPOOL> {0}, Active", ObjectID);
            }
            return (PoolGroupStateActive == _state);
        }

        internal bool Prune()
        {
            // must only call from DbConnectionFactory.PruneConnectionPoolGroups on background timer thread
            // must lock(DbConnectionFactory._connectionPoolGroups.SyncRoot) before calling ReadyToRemove
            //     to avoid conflict with DbConnectionFactory.CreateConnectionPoolGroup replacing pool entry
            lock (this)
            {
                if (_poolCollection.Count > 0)
                {
                    var newPoolCollection = new ConcurrentDictionary<DbConnectionPoolIdentity, IDbConnectionPool>();

                    foreach (KeyValuePair<DbConnectionPoolIdentity, IDbConnectionPool> entry in _poolCollection)
                    {
                        IDbConnectionPool pool = entry.Value;
                        if (pool != null)
                        {
                            // Actually prune the pool if there are no connections in the pool and no errors occurred.
                            // Empty pool during pruning indicates zero or low activity, but
                            //  an error state indicates the pool needs to stay around to
                            //  throttle new connection attempts.
                            if ((!pool.ErrorOccurred) && (0 == pool.Count))
                            {
                                // Order is important here.  First we remove the pool
                                // from the collection of pools so no one will try
                                // to use it while we're processing and finally we put the
                                // pool into a list of pools to be released when they
                                // are completely empty.
                                SqlConnectionFactory connectionFactory = pool.ConnectionFactory;

                                connectionFactory.QueuePoolForRelease(pool, false);
                            }
                            else
                            {
                                newPoolCollection.TryAdd(entry.Key, entry.Value);
                            }
                        }
                    }
                    _poolCollection = newPoolCollection;
                }

                // must be pruning thread to change state and no connections
                // otherwise pruning thread risks making entry disabled soon after user calls ClearPool
                if (0 == _poolCollection.Count)
                {
                    if (PoolGroupStateActive == _state)
                    {
                        _state = PoolGroupStateIdle;
                        SqlClientEventSource.Log.TryTraceEvent("<prov.DbConnectionPoolGroup.ClearInternal|RES|INFO|CPOOL> {0}, Idle", ObjectID);
                    }
                    else if (PoolGroupStateIdle == _state)
                    {
                        _state = PoolGroupStateDisabled;
                        SqlClientEventSource.Log.TryTraceEvent("<prov.DbConnectionPoolGroup.ReadyToRemove|RES|INFO|CPOOL> {0}, Disabled", ObjectID);
                    }
                }
                return (PoolGroupStateDisabled == _state);
            }
        }
    }
}
