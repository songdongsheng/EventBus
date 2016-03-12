using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace EventBus
{
    /// <summary>
    /// The SimpleEventBus class is a simple and fast IEventBus implemention.
    /// </summary>
    /// <remarks>The event may be processed out of the delivery order under heavy load.</remarks>
    public class SimpleEventBus : IEventBus
    {
        private const long DefaultMaxQueueDepth = 1024 * 1024;
        private static readonly SimpleEventBus DefaultEventBus = new SimpleEventBus(DefaultMaxQueueDepth);

        private List<EventHandler> _eventHandlerList = new List<EventHandler>();
        private readonly object _eventHandlerLock = new object();

        // Interlocked operation cause the performance drop at least 10% !.
        private long _pendingEventNumber;

        private readonly long _maxPendingEventNumber;

        // This counter cause the performance drop at least 5% !
        // private long _totalEventNumber;

        /// <summary>
        /// Get the global SimpleEventBus instance.
        /// </summary>
        /// <returns>The global SimpleEventBus instance</returns>
        public static SimpleEventBus GetDefaultEventBus()
        {
            return DefaultEventBus;
        }

        /// <summary>
        /// The pending event number which does not yet dispatched.
        /// </summary>
        public long PendingEventNumber => Math.Max(Interlocked.Read(ref _pendingEventNumber), 0);

        // The total event number which post to the event bus.
        // This counter cause the performance drop at least 5% !
        // public long TotalEventNumber => Interlocked.Read(ref _totalEventNumber);

        /// <summary>
        /// The constructor of SimpleEventBus.
        /// </summary>
        /// <param name="maxPendingEventNumber">The maximum pending event number which does not yet dispatched</param>
        public SimpleEventBus(long maxPendingEventNumber)
        {
            _maxPendingEventNumber = maxPendingEventNumber > 0 ? maxPendingEventNumber : DefaultMaxQueueDepth;
        }

        /// <summary>
        /// Post an event to the event bus, dispatched after the specific time.
        /// </summary>
        /// <remarks>The event may be processed out of the delivery order under heavy load.</remarks>
        /// <param name="eventObject">The event object</param>
        /// <param name="dispatchDelay">The delay time before dispatch this event</param>
        public void Post(object eventObject, TimeSpan dispatchDelay)
        {
            int dispatchDelayMs = (int)dispatchDelay.TotalMilliseconds;

            while (Interlocked.Read(ref _pendingEventNumber) >= _maxPendingEventNumber)
            {
                Trace.TraceWarning("Too many events in the EventBus, pendingEventNumber={0}, maxPendingEventNumber={1}{2}PendingEvent='{3}', dispatchDelay={4}ms",
                    PendingEventNumber, _maxPendingEventNumber, Environment.NewLine, eventObject, dispatchDelayMs);
                Thread.Sleep(16);
            }

            if (dispatchDelayMs >= 1)
            {
                Task.Delay(dispatchDelayMs).ContinueWith(task =>
                {
                    DispatchMessage(eventObject);
                });
            }
            else
            {
                Task.Run(() => DispatchMessage(eventObject));
            }

            Interlocked.Increment(ref _pendingEventNumber);
            // Interlocked.Increment(ref _totalEventNumber);
        }

        private void DispatchMessage(object eventObject)
        {
            try
            {
                // ReSharper disable once ForCanBeConvertedToForeach
                for (int i = 0; i < _eventHandlerList.Count; i++)
                {
                    // ReSharper disable once InconsistentlySynchronizedField
                    EventHandler record = _eventHandlerList[i];
                    if (eventObject == null || record.ParameterType.IsInstanceOfType(eventObject))
                    {
                        Task.Run(() =>
                        {
                            try
                            {
                                record.MethodInfo.Invoke(record.Handler, new[] { eventObject });
                            }
                            catch (Exception ie)
                            {
                                Trace.TraceWarning("Event handler (class '{0}@{1}', method '{2}') failed: {3}{4}{5}{4}eventObject: {6}",
                                    record.Handler.GetType(), record.Handler.GetHashCode(), record.MethodInfo,
                                    ie.Message, Environment.NewLine, ie.StackTrace, eventObject);
                            }
                        });
                    }
                }
            }
            catch (Exception de)
            {
                Trace.TraceError("Dispatch event ({0}) failed: {1}{2}{3}",
                    eventObject, de.Message, Environment.NewLine, de.StackTrace);
            }
            finally
            {
                Interlocked.Decrement(ref _pendingEventNumber);
            }
        }

        /// <summary>
        /// Register event handlers in the handler instance.
        ///
        /// One handler instance may have many event handler methods.
        /// These methods have EventSubscriberAttribute contract and exactly one parameter.
        /// </summary>
        /// <remarks>The handler may be invoked out of the event delivery order under heavy load.</remarks>
        /// <param name="handler">The instance of event handler class</param>
        public void Register(object handler)
        {
            if (handler == null)
            {
                return;
            }

            MethodInfo[] miList = handler.GetType().GetMethods();
            lock (_eventHandlerLock)
            {
                // Don't allow register multiple times.
                if (_eventHandlerList.Any(record => record.Handler == handler))
                {
                    return;
                }

                List<EventHandler> newList = null;
                foreach (MethodInfo mi in miList)
                {
                    EventSubscriberAttribute attribute = mi.GetCustomAttribute<EventSubscriberAttribute>();
                    if (attribute != null)
                    {
                        ParameterInfo[] piList = mi.GetParameters();
                        if (piList.Length == 1)
                        {
                            // OK, we got valid handler, create newList as needed
                            if (newList == null)
                            {
                                newList = new List<EventHandler>(_eventHandlerList);
                            }
                            newList.Add(new EventHandler(handler, mi, piList[0].ParameterType));
                        }
                    }
                }

                // OK, we have new handler registered
                if (newList != null)
                {
                    _eventHandlerList = newList;
                }
            }
        }

        /// <summary>
        /// Deregister event handlers belong to the handler instance.
        ///
        /// One handler instance may have many event handler methods.
        /// These methods have EventSubscriberAttribute contract and exactly one parameter.
        /// </summary>
        /// <param name="handler">The instance of event handler class</param>
        public void Deregister(object handler)
        {
            if (handler == null)
            {
                return;
            }

            lock (_eventHandlerLock)
            {
                bool needAction = _eventHandlerList.Any(record => record.Handler == handler);

                if (needAction)
                {
                    List<EventHandler> newList = _eventHandlerList.Where(record => record.Handler != handler).ToList();
                    _eventHandlerList = newList;
                }
            }
        }

        private class EventHandler
        {
            public EventHandler(object handler, MethodInfo methodInfo, Type parameterType)
            {
                Handler = handler;
                MethodInfo = methodInfo;
                ParameterType = parameterType;
            }

            public object Handler { get; }

            public MethodInfo MethodInfo { get; }

            public Type ParameterType { get; }
        }
    }
}
