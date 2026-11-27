# ScrobblerBrainz (Patched Fork)

A MusicBee plug-in to scrobble tracks to ListenBrainz.

> **Attribution:** This is a patched fork of the original [ScrobblerBrainz](https://github.com/karaluh/ScrobblerBrainz) created by [karaluh](https://github.com/karaluh). All original credit goes to the upstream author.

## Changes in v2.0.2
- Enabled TLS 1.2 support so ListenBrainz API requests succeed on modern endpoints.
- Switched scrobble and `playing_now` submissions to asynchronous background HTTP requests with retry handling and error logging (`error.log`).
- Added offline scrobble queuing (`scrobbles/*.json`) with automatic resend on MusicBee startup when a submission fails.
- Added proper JSON string escaping and multi-artist MBID parsing (Xiph comments and delimited tags).
- Cleaned up Visual Studio solution/project files to remove hardcoded local paths.

## Restrictions
1. Scrobbles tracks shorter than 30 s. (last.fm scrobbler doesn't do this).
2. No loved tracks/playcount sync.
3. Doesn't scrobble tracks played on portable devices.
4. No feature to disable podcast scrobbling.

## Download
Download the latest `mb_ScrobblerBrainz.zip` from the [Releases](../../releases) page of this repository.

## Usage
1. Get MusicBee: https://getmusicbee.com/downloads/
2. Get a MusicBrainz account: https://musicbrainz.org/register?uri=%2F
3. Get a ListenBrainz account: https://listenbrainz.org/login/
4. Download `mb_ScrobblerBrainz.zip` from the [Releases](../../releases) page
5. Open MusicBee and go to *Edit -> Edit preferences -> Plugins*
6. Press the *Add plugin...* button
7. Choose the downloaded ZIP file and press *Open*
8. Get your ListenBrainz user token: https://listenbrainz.org/profile/
9. Input your user token into the plugin settings box
10. Save the settings and you're good to go
