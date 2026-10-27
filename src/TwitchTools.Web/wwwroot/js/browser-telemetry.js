/**
 * Minimal browser tracing. Spans are sent as OTLP/JSON to the same-origin endpoint in the script
 * tag's data-endpoint (TelemetryController forwards them to the collector), so no SDK or CORS.
 *
 * Records a page.load span, a browser.error span for uncaught errors and rejections, and exposes
 * window.twitchToolsTelemetry.startSpan() for page scripts (see Views/Chat/Index.cshtml).
 */
(() => {
    const endpoint = document.currentScript && document.currentScript.dataset.endpoint;
    if (!endpoint) {
        return;
    }

    const serviceName = 'twitch-tools-browser';
    const maxQueued = 500;
    const flushIntervalMs = 5000;
    const pageId = randomHex(8);
    let queue = [];

    function randomHex(bytes) {
        const values = crypto.getRandomValues(new Uint8Array(bytes));
        return Array.from(values, (b) => b.toString(16).padStart(2, '0')).join('');
    }

    // OTLP wants nanoseconds as a string; microsecond precision keeps the number exact.
    function nowNanos() {
        return (BigInt(Math.round((performance.timeOrigin + performance.now()) * 1000)) * 1000n).toString();
    }

    function toAttributes(attributes) {
        const result = [];
        for (const [key, value] of Object.entries(attributes || {})) {
            if (value === undefined || value === null) {
                continue;
            }

            if (typeof value === 'boolean') {
                result.push({ key, value: { boolValue: value } });
            } else if (typeof value === 'number' && Number.isInteger(value)) {
                result.push({ key, value: { intValue: String(value) } });
            } else if (typeof value === 'number') {
                result.push({ key, value: { doubleValue: value } });
            } else {
                result.push({ key, value: { stringValue: String(value) } });
            }
        }
        return result;
    }

    function startSpan(name, attributes, options) {
        const opts = options || {};
        const span = {
            traceId: opts.traceId || randomHex(16),
            spanId: randomHex(8),
            parentSpanId: opts.parentSpanId,
            name,
            kind: opts.kind || 1,
            startTimeUnixNano: opts.startTimeUnixNano || nowNanos(),
            attributes: Object.assign({ 'url.path': location.pathname, 'page.id': pageId }, attributes),
            events: [],
            status: { code: 0 }
        };
        let ended = false;

        return {
            setAttribute(key, value) {
                span.attributes[key] = value;
            },
            addEvent(eventName, eventAttributes) {
                span.events.push({ timeUnixNano: nowNanos(), name: eventName, attributes: toAttributes(eventAttributes) });
            },
            setError(message) {
                span.status = { code: 2, message: String(message || '') };
            },
            child(childName, childAttributes, childOptions) {
                return startSpan(childName, childAttributes, Object.assign({}, childOptions, { traceId: span.traceId, parentSpanId: span.spanId }));
            },
            // W3C trace context, so the server span for a fetch joins this trace.
            traceparent() {
                return '00-' + span.traceId + '-' + span.spanId + '-01';
            },
            end(endTimeUnixNano) {
                if (ended) {
                    return;
                }
                ended = true;
                if (queue.length >= maxQueued) {
                    queue.shift();
                }
                queue.push(Object.assign({}, span, {
                    endTimeUnixNano: endTimeUnixNano || nowNanos(),
                    attributes: toAttributes(span.attributes)
                }));
            }
        };
    }

    function flush(useBeacon) {
        if (queue.length === 0) {
            return;
        }

        const body = JSON.stringify({
            resourceSpans: [{
                resource: { attributes: toAttributes({ 'service.name': serviceName }) },
                scopeSpans: [{ scope: { name: serviceName }, spans: queue }]
            }]
        });
        queue = [];

        if (useBeacon && navigator.sendBeacon) {
            navigator.sendBeacon(endpoint, new Blob([body], { type: 'application/json' }));
            return;
        }

        fetch(endpoint, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body,
            credentials: 'same-origin',
            keepalive: true,
            redirect: 'manual'
        }).catch(() => {
            // Telemetry is best effort.
        });
    }

    function recordError(type, message, stack) {
        const span = startSpan('browser.error', { 'exception.type': type });
        span.addEvent('exception', { 'exception.type': type, 'exception.message': message, 'exception.stacktrace': stack });
        span.setError(message);
        span.end();
    }

    window.addEventListener('error', (event) => {
        recordError(event.error && event.error.name || 'Error', event.message, event.error && event.error.stack);
    });

    window.addEventListener('unhandledrejection', (event) => {
        const reason = event.reason;
        recordError(reason && reason.name || 'UnhandledRejection', reason && reason.message || String(reason), reason && reason.stack);
    });

    window.addEventListener('load', () => {
        // loadEventEnd is only set once the load handlers have finished.
        setTimeout(() => {
            const nav = performance.getEntriesByType('navigation')[0];
            if (!nav) {
                return;
            }
            const toNanos = (ms) => (BigInt(Math.round((performance.timeOrigin + ms) * 1000)) * 1000n).toString();
            const span = startSpan('page.load', {
                'navigation.type': nav.type,
                'http.response.status_code': nav.responseStatus,
                'page.ttfb_ms': Math.round(nav.responseStart),
                'page.dom_content_loaded_ms': Math.round(nav.domContentLoadedEventEnd),
                'page.load_ms': Math.round(nav.loadEventEnd)
            }, { startTimeUnixNano: toNanos(0) });
            span.end(toNanos(nav.loadEventEnd));
        }, 0);
    });

    setInterval(() => flush(false), flushIntervalMs);
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'hidden') {
            flush(true);
        }
    });
    window.addEventListener('pagehide', () => flush(true));

    window.twitchToolsTelemetry = { startSpan, flush };
})();
