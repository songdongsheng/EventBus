using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace EventBus
{
    /// <summary>
    /// The OrderedEventBus class is a simple and fast IEventBus implemention which processes event in the delivery order.
    /// </summary>
    /// <remarks>If you do not need the event processed in the delivery order, use SimpleEventBus instead.</remarks>
    public class OrderedEventBus : IEventBus, IDisposable
    {
        private const int DefaultMaxPendingEventNumber = 1024 * 1024;

        private bool _isDisposed;
        private static readonly OrderedEventBus DefaultEventBus = new OrderedEventBus(DefaultMaxPendingEventNumber);

        private List<EventHandlerHolder> _eventHandlerList = new List<EventHandlerHolder>();
        private readonly object _eventHandlerLock = new object();

        private readonly BlockingCollection<object> _eventQueue;

        /// <summary>
        /// The constructor of OrderedEventBus.
        /// </summary>
        /// <param name="maxPendingEventNumber">The maximum pending event number which does not yet dispatched</param>
        public OrderedEventBus(int maxPendingEventNumber)
        {
            _eventQueue = new BlockingCollection<object>(
                maxPendingEventNumber > 0
                ? maxPendingEventNumber
                : DefaultMaxPendingEventNumber);

            Thread dispatchThread = new Thread(DispatchMessage) {IsBackground = true};
            dispatchThread.Name = "OrderedEventBus-Thread-" + dispatchThread.ManagedThreadId;
            dispatchThread.Start();
        }

        /// <summary>
        /// The pending event number which does not yet dispatched.
        /// </summary>
        public int PendingEventNumber => Math.Max(_eventQueue.Count, 0);

        /// <summary>
        /// Post an event to the event bus, dispatched after the specific time.
        /// </summary>
        /// <remarks>If you do not need the event processed in the delivery order, use SimpleEventBus instead.</remarks>
        /// <param name="eventObject">The event object</param>
        /// <param name="dispatchDelay">The delay time before dispatch this event</param>
        public void Post(object eventObject, TimeSpan dispatchDelay)
        {
            int dispatchDelayMs = (int) dispatchDelay.TotalMilliseconds;

            if (dispatchDelayMs >= 1)
            {
                Task.Delay(dispatchDelayMs).ContinueWith(task => _eventQueue.Add(eventObject));
            }
            else
            {
                _eventQueue.Add(eventObject);
            }
        }

        /// <summary>
        /// Register event handlers in the handler instance.
        /// One handler instance may have many event handler methods.
        /// These methods have EventSubscriberAttribute contract and exactly one parameter.
        /// </summary>
        /// <remarks>If you do not need the event processed in the delivery order, use SimpleEventBus instead.</remarks>
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

                List<EventHandlerHolder> newList = null;
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
                                newList = new List<EventHandlerHolder>(_eventHandlerList);
                            }
                            newList.Add(new EventHandlerHolder(handler, mi, piList[0].ParameterType));
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
                    List<EventHandlerHolder> newList = _eventHandlerList.Where(record => record.Handler != handler).ToList();
                    _eventHandlerList = newList;
                }
            }
        }

        /// <summary>
        /// Get the global OrderedEventBus instance.
        /// </summary>
        /// <returns>The global OrderedEventBus instance</returns>
        public static OrderedEventBus GetDefaultEventBus()
        {
            return DefaultEventBus;
        }

        private void DispatchMessage()
        {
            while (true)
            {
                object eventObject = null;

                try
                {
                    eventObject = _eventQueue.Take();
                    InvokeEventHandler(eventObject);
                }
                catch (Exception de)
                {
                    if (de is ObjectDisposedException)
                    {
                        return;
                    }

                    Trace.TraceError("Dispatch event ({0}) failed: {1}{2}{3}",
                        eventObject, de.Message, Environment.NewLine, de.StackTrace);
                }
            }
        }

        private void InvokeEventHandler(object eventObject)
        {
            List<Task> taskList = null;
            // ReSharper disable once ForCanBeConvertedToForeach
            for (int i = 0; i < _eventHandlerList.Count; i++)
            {
                // ReSharper disable once InconsistentlySynchronizedField
                EventHandlerHolder record = _eventHandlerList[i];
                if (eventObject == null || record.ParameterType.IsInstanceOfType(eventObject))
                {
                    Task task = Task.Run(() => record.MethodInfo.Invoke(record.Handler, new[] { eventObject }));
                    if (taskList == null) taskList = new List<Task>();
                    taskList.Add(task);
                    //record.MethodInfo.Invoke(record.Handler, new[] { eventObject });
                }
            }
            if (taskList != null)
            {
                Task.WaitAll(taskList.ToArray());
            }
        }

        /// <summary>
        /// Releases resources used by the <see cref="EventBus.OrderedEventBus"/> instance.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases resources used by the <see cref="EventBus.OrderedEventBus"/> instance.
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    _eventQueue.Dispose();
                }
                _isDisposed = true;
            }
        }
    }
}
