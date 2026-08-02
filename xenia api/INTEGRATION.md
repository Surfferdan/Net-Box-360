# Integration Notes

The API currently registers a placeholder facade:

- XeniaManager.Api/Adapters/NotConfiguredLegacyFacade.cs

To wire existing working backend logic:

1. Create a class in your backend assembly that implements IXeniaManagerLegacyFacade.
2. In each method, delegate to the existing profile/achievement/save/config/launcher logic from Xenia Manager.
3. Replace registration in XeniaManager.Api/Program.cs:

```csharp
builder.Services.AddXeniaManagerLegacyAdapters<YourRealLegacyFacade>();
```

This keeps backend parsing/discovery behavior intact while exposing it through REST and WebSocket.

## API Surface

- GET /api/profiles
- GET /api/profiles/{id}
- POST /api/profiles
- PUT /api/profiles/{id}
- DELETE /api/profiles/{id}
- GET /api/profiles/{id}/achievements
- GET /api/profiles/{id}/saves
- POST /api/saves/backup
- POST /api/saves/restore
- GET /api/config
- PUT /api/config
- POST /api/xenia/start
- POST /api/xenia/stop
- GET /api/xenia/status
- WS /ws/events
