# Google Photos setup

The desktop app uses the Google Photos Picker API so users explicitly select photos rather than granting broad library access.

To enable this provider:

1. Create or select a Google Cloud project.
2. Enable the Google Photos Picker API.
3. Create an OAuth 2.0 desktop application client.
4. Add the client ID to the Handshake settings configuration when that settings field is available.

The required scope is `https://www.googleapis.com/auth/photospicker.mediaitems.readonly`. The app must open the returned picker session URL, wait for the user to finish selecting media, poll the session, and download each selected media item with the OAuth bearer token.

Until a client ID is supplied, use local files or drag-and-drop. No Google credential is checked into this repository.
