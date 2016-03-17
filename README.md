Simple Event Bus Library for .NET
=================================

Questions & Comments
--------------------

Any feedback is welcome, please use the Issues on this repository.

Build Status
------------

|CoreCLR 1.0 |.NET Framework 4.6 |.NET Framework 4.5 |
|:---------: |:-----------------:|:-----------------:|
| ![](https://ci.appveyor.com/api/projects/status/github/songdongsheng/eventbus?branch=master&svg=true) | ![](https://ci.appveyor.com/api/projects/status/github/songdongsheng/eventbus?branch=master&svg=true) | ![](https://ci.appveyor.com/api/projects/status/github/songdongsheng/eventbus?branch=master&svg=true) |

Basic usage
-----------

    private void SimpleTest()
    {
        // OrderedEventBus eventBus = OrderedEventBus.GetDefaultEventBus();
        SimpleEventBus eventBus = SimpleEventBus.GetDefaultEventBus();
        eventBus.Register(this);
        eventBus.Post("msg", TimeSpan.Zero);
        eventBus.Post("xxx", TimeSpan.FromSeconds(2));
        eventBus.Post(new RarEvent("session-01", "ggsn-01"), TimeSpan.FromSeconds(1));
        while(eventBus.PendingEventNumber > 0)
        {
            Thread.Sleep(100);
        }
        eventBus.Deregister(this);
    }

    [EventSubscriber]
    public void HandleEvent(RarEvent rarEvent)
    {
        Trace.TraceInformation("Got RAR event: {0}", rarEvent);
    }

    [EventSubscriber]
    public void HandleEvent(string message)
    {
        Trace.TraceInformation("Got message event: {0}", message);
    }

    internal class RarEvent
    {
        public RarEvent(string sessionId, string hostId)
        {
            SessionId = sessionId;
            HostId = hostId;
        }

        public RarEvent(string sessionId, string hostId, string peerId)
        {
            SessionId = sessionId;
            HostId = hostId;
            PeerId = peerId;
        }

        public string SessionId { get; }

        public string HostId { get; }

        public string PeerId { get; }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(128);
            sb.Append("{sessionId='").AppendFormat(SessionId);
            sb.Append("', hostId='").Append(HostId);
            if (!string.IsNullOrWhiteSpace(PeerId))
            {
                sb.Append("', peerId='").Append(PeerId);
            }
            sb.Append("'}");
            return sb.ToString();
        }
    }

Building from source
--------------------

    cd EventBus

    msbuild EventBus.csproj /t:Rebuild /p:Platform=AnyCPU /p:Configuration=Release
    msbuild EventBus.csproj /t:Rebuild /p:Platform=AnyCPU /p:Configuration=Release46
    msbuild EventBus.csproj /t:Rebuild /p:Platform=AnyCPU /p:Configuration=Release45

    dotnet restore
    dotnet build -c Release
