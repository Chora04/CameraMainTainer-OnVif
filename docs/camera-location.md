# Camera location

Camera settings store optional WGS84 latitude, longitude and a street address. Both coordinates must be supplied together: latitude -90..90, longitude -180..180. Clearing both fields removes coordinates. The address can be edited manually and is saved with the camera, not on each health check.

The **Determine street** action sends only coordinates to the configured reverse geocoder. IP addresses, camera names and credentials are never included. Reverse geocoding returns the nearest mapped address; it is not a precise location guarantee.

## Provider

`Geocoding:ReverseUrl` in `src/CameraMaintainer.Web/appsettings.json` currently points to `https://nominatim.openstreetmap.org/reverse`.

Public Nominatim use is interactive only, with an identifying User-Agent, at least 1.1 seconds between requests across accounts, a bounded seven-day lookup cache and OpenStreetMap attribution. Saved addresses remain in the camera database. No monitoring jobs, automatic coordinate watchers or bulk geocoding use this API.

Policy: https://operations.osmfoundation.org/policies/nominatim/

## Local replacement

Run a local Nominatim-compatible reverse-geocoding service and replace the URL, for example `http://localhost:8080/reverse`. HTTPS endpoints and HTTP loopback endpoints are accepted. An environment variable can also be used: `Geocoding__ReverseUrl`. Restart the application after changing deployment configuration.

No UI changes, coordinate migration or changes to the monitoring queue are needed for a compatible service. A tile-only map server cannot resolve street names: it also needs a searchable address dataset and reverse-geocoding API. For a different API format, adapt `CameraGeocodingService` only. Set the URL to an empty string to disable external lookups while keeping manual location editing.

## History previews

History camera cards request the existing authorized ONVIF frame endpoint with `preferCached=true`. Frames load lazily and reuse the bounded snapshot cache. Loaded images are retained across history counter refreshes. No camera-view permission means no frame request. If neither a cached frame nor a new ONVIF snapshot is available, the card shows an unavailable state; it does not invent a frame or start a video stream.
