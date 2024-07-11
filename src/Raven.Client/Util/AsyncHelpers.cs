// -----------------------------------------------------------------------
//  <copyright file="AsyncHelpers.cs" company="Hibernating Rhinos LTD">
//      Copyright (coffee) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Extensions;
using Sparrow;

namespace Raven.Client.Util
{
    public static class AsyncHelpers
    {
        public static bool UseTaskAwaiterWhenNoSynchronizationContextIsAvailable = true;

        private struct ExclusiveSynchronizationContextResetBehavior : IResetSupport<ExclusiveSynchronizationContext>
        {
            public void Reset(ExclusiveSynchronizationContext value)
            {
                value.Reset();
            }
        }

        private static ObjectPool<ExclusiveSynchronizationContext, ExclusiveSynchronizationContextResetBehavior> _pool = new ObjectPool<ExclusiveSynchronizationContext, ExclusiveSynchronizationContextResetBehavior>(() => new ExclusiveSynchronizationContext(), 10);

        public static void RunSync(Func<Task> task)
        {
            var oldContext = SynchronizationContext.Current;
            var sw = Stopwatch.StartNew();

            // Do we have an active synchronization context?
            if (UseTaskAwaiterWhenNoSynchronizationContextIsAvailable && oldContext == null)
            {
                // We can run synchronously without any issue.
                try
                {
                    task().GetAwaiter().GetResult();
                }
                catch (AggregateException ex)
                {
                    HandleException(ex, sw);
                }
                return;
            }

            var synch = _pool.Allocate();

            SynchronizationContext.SetSynchronizationContext(synch);
            try
            {
                synch.Post(async _ =>
                {
                    try
                    {
                        await task().ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        synch.InnerException = e;
                        throw;
                    }
                    finally
                    {
                        synch.EndMessageLoop();
                    }
                }, null);
                synch.BeginMessageLoop();
            }
            catch (AggregateException ex)
            {
                HandleException(ex, sw);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(oldContext);
            }

            _pool.Free(synch);
        }

        public static T RunSync<T>(Func<Task<T>> task)
        {
            var oldContext = SynchronizationContext.Current;
            var sw = Stopwatch.StartNew();

            // Do we have an active synchronization context?
            if (UseTaskAwaiterWhenNoSynchronizationContextIsAvailable && oldContext == null)
            {
                // We can run synchronously without any issue.
                try
                {
                    return task().GetAwaiter().GetResult();
                }
                catch (AggregateException ex)
                {
                    HandleException(ex, sw);
                }
            }

            var result = default(T);
            var synch = _pool.Allocate();

            SynchronizationContext.SetSynchronizationContext(synch);
            try
            {
                synch.Post(async _ =>
                {
                    try
                    {
                        result = await task().ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        synch.InnerException = e;
                        throw;
                    }
                    finally
                    {
                        sw.Stop();
                        synch.EndMessageLoop();
                    }
                }, null);
                synch.BeginMessageLoop();
            }
            catch (AggregateException ex)
            {
                HandleException(ex, sw);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(oldContext);
            }

            _pool.Free(synch);

            return result;
        }

        internal static T RunSync<T>(Func<ValueTask<T>> taskFactory)
        {
            var oldContext = SynchronizationContext.Current;
            var sw = Stopwatch.StartNew();

            var task = taskFactory();

            // Do we have an active synchronization context?
            if (UseTaskAwaiterWhenNoSynchronizationContextIsAvailable && oldContext == null && task.IsCompleted)
            {
                // We can run synchronously without any issue.
                try
                {
                    return task.GetAwaiter().GetResult();
                }
                catch (AggregateException ex)
                {
                    HandleException(ex, sw);
                }
            }

            var result = default(T);
            var synch = _pool.Allocate();

            SynchronizationContext.SetSynchronizationContext(synch);
            try
            {
                synch.Post(async _ =>
                {
                    try
                    {
                        result = await task.ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        synch.InnerException = e;
                        throw;
                    }
                    finally
                    {
                        sw.Stop();
                        synch.EndMessageLoop();
                    }
                }, null);
                synch.BeginMessageLoop();
            }
            catch (AggregateException ex)
            {
                HandleException(ex, sw);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(oldContext);
            }

            _pool.Free(synch);

            return result;
        }

#if !NETSTANDARD2_0
        [DoesNotReturn]
#endif
        private static void HandleException(AggregateException ex, Stopwatch sw)
        {
            var exception = ex.ExtractSingleInnerException();
            if (exception is OperationCanceledException)
                throw new TimeoutException("Operation timed out after: " + sw.Elapsed, ex);

            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        private sealed class ExclusiveSynchronizationContext : SynchronizationContext
        {
            private readonly AutoResetEvent _workItemsWaiting = new AutoResetEvent(false);
            private readonly ConcurrentQueue<(SendOrPostCallback Callback, object State)> _items = new ConcurrentQueue<(SendOrPostCallback, object)>();

            private bool _done;
            public Exception InnerException { get; set; }

            public override void Send(SendOrPostCallback d, object state)
            {
                throw new NotSupportedException("We cannot send to our same thread");
            }

            public override void Post(SendOrPostCallback d, object state)
            {
                _items.Enqueue((d, state));
                _workItemsWaiting.Set();
            }

            public void Reset()
            {
                _workItemsWaiting.Reset();
                _done = false;
                InnerException = null;
                while (_items.TryDequeue(out var dummy))
                {
                    // Drain queue in case of exceptions.
                }
            }

            public void EndMessageLoop()
            {
                Post(_ => _done = true, null);
            }

            public void BeginMessageLoop()
            {
                OperationStarted();

                // Start to process in a loop
                while (!_done)
                {
                    // If the queue is empty, we wait.
                    if (_items.IsEmpty)
                        _workItemsWaiting.WaitOne();

                    // Queue is no longer empty (unless someone won) therefore we are ready to process.
                    while (_items.TryDequeue(out var work))
                    {
                        // Execute the operation.
                        work.Callback(work.State);
                        if (InnerException != null) // the method threw an exception
                        {
                            throw new AggregateException("AsyncHelpers.Run method threw an exception.", InnerException);
                        }
                    }
                }

                OperationCompleted();
            }

            public override SynchronizationContext CreateCopy()
            {
                return this;
            }
        }
    }
}
