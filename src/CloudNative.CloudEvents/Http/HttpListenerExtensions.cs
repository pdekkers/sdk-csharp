﻿// Copyright 2021 Cloud Native Foundation.
// Licensed under the Apache 2.0 license.
// See LICENSE file in the project root for full license information.

using CloudNative.CloudEvents.Core;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mime;
using System.Threading.Tasks;

namespace CloudNative.CloudEvents.Http
{
    /// <summary>
    /// Extension methods for <see cref="HttpListener"/> and related classes
    /// (<see cref="HttpListenerResponse"/> etc).
    /// </summary>
    public static class HttpListenerExtensions
    {
        /// <summary>
        /// Copies a <see cref="CloudEvent"/> into an <see cref="HttpListenerResponse" />.
        /// </summary>
        /// <param name="cloudEvent">The CloudEvent to copy. Must not be null, and must be a valid CloudEvent.</param>
        /// <param name="destination">The response to copy the CloudEvent to. Must not be null.</param>
        /// <param name="contentMode">Content mode (structured or binary)</param>
        /// <param name="formatter">The formatter to use within the conversion. Must not be null.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static async Task CopyToHttpListenerResponseAsync(this CloudEvent cloudEvent, HttpListenerResponse destination,
            ContentMode contentMode, CloudEventFormatter formatter)
        {
            Validation.CheckCloudEventArgument(cloudEvent, nameof(cloudEvent));
            Validation.CheckNotNull(destination, nameof(destination));
            Validation.CheckNotNull(formatter, nameof(formatter));

            ReadOnlyMemory<byte> content;
            ContentType contentType;
            switch (contentMode)
            {
                case ContentMode.Structured:
                    content = formatter.EncodeStructuredModeMessage(cloudEvent, out contentType);
                    break;
                case ContentMode.Binary:
                    content = formatter.EncodeBinaryModeEventData(cloudEvent);
                    contentType = MimeUtilities.CreateContentTypeOrNull(cloudEvent.DataContentType);
                        destination.ContentType = cloudEvent.DataContentType?.ToString() ?? "application/json";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(contentMode), $"Unsupported content mode: {contentMode}");
            }
            if (contentType is object)
            {
                destination.ContentType = contentType.ToString();
            }
            else if (content.Length != 0)
            {
                throw new ArgumentException(Strings.ErrorContentTypeUnspecified, nameof(cloudEvent));
            }

            // Map headers in either mode.
            // Including the headers in structured mode is optional in the spec (as they're already within the body) but
            // can be useful.            
            destination.Headers.Add(HttpUtilities.SpecVersionHttpHeader, HttpUtilities.EncodeHeaderValue(cloudEvent.SpecVersion.VersionId));
            foreach (var attributeAndValue in cloudEvent.GetPopulatedAttributes())
            {
                var attribute = attributeAndValue.Key;
                var value = attributeAndValue.Value;
                // The content type is already handled based on the content mode.
                if (attribute != cloudEvent.SpecVersion.DataContentTypeAttribute)
                {
                    string headerValue = HttpUtilities.EncodeHeaderValue(attribute.Format(value));
                    destination.Headers.Add(HttpUtilities.HttpHeaderPrefix + attribute.Name, headerValue);
                }
            }

            await BinaryDataUtilities.CopyToStreamAsync(content, destination.OutputStream).ConfigureAwait(false);
        }

        /// <summary>
        /// Copies a <see cref="CloudEvent"/> batch into an <see cref="HttpListenerResponse" />.
        /// </summary>
        /// <param name="cloudEvents">The CloudEvent batch to copy. Must not be null, and must be a valid CloudEvent.</param>
        /// <param name="destination">The response to copy the CloudEvent to. Must not be null.</param>
        /// <param name="formatter">The formatter to use within the conversion. Must not be null.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static async Task CopyToHttpListenerResponseAsync(this IReadOnlyList<CloudEvent> cloudEvents,
            HttpListenerResponse destination, CloudEventFormatter formatter)
        {
            Validation.CheckCloudEventBatchArgument(cloudEvents, nameof(cloudEvents));
            Validation.CheckNotNull(destination, nameof(destination));
            Validation.CheckNotNull(formatter, nameof(formatter));

            // TODO: Validate that all events in the batch have the same version?
            // See https://github.com/cloudevents/spec/issues/807

            ReadOnlyMemory<byte> content = formatter.EncodeBatchModeMessage(cloudEvents, out var contentType);
            destination.ContentType = contentType.ToString();
            await BinaryDataUtilities.CopyToStreamAsync(content, destination.OutputStream).ConfigureAwait(false);
        }

        /// <summary>
        /// Indicates whether this HttpListenerRequest holds a CloudEvent
        /// </summary>
        public static bool IsCloudEvent(this HttpListenerRequest httpListenerRequest) =>
            HasCloudEventsContentType(httpListenerRequest) ||
            httpListenerRequest.Headers[HttpUtilities.SpecVersionHttpHeader] is object;

        /// <summary>
        /// Converts this listener request into a CloudEvent object, with the given extension attributes.
        /// </summary>
        /// <param name="httpListenerRequest">The listener request to convert. Must not be null.</param>
        /// <param name="formatter">The event formatter to use to parse the CloudEvent. Must not be null.</param>
        /// <param name="extensionAttributes">The extension attributes to use when parsing the CloudEvent. May be null.</param>
        /// <returns>A reference to a validated CloudEvent instance.</returns>
        public static Task<CloudEvent> ToCloudEventAsync(this HttpListenerRequest httpListenerRequest,
            CloudEventFormatter formatter, params CloudEventAttribute[] extensionAttributes) =>
            // No async/await here, as the delegation is to *such* a similar method (same name, same parameter names)
            // that the stack trace will still be very easy to understand.
            ToCloudEventAsync(httpListenerRequest, formatter, (IEnumerable<CloudEventAttribute>) extensionAttributes);

        /// <summary>
        /// Converts this listener request into a CloudEvent object, with the given extension attributes.
        /// </summary>
        /// <param name="httpListenerRequest">The listener request to convert. Must not be null.</param>
        /// <param name="formatter">The event formatter to use to parse the CloudEvent. Must not be null.</param>
        /// <param name="extensionAttributes">The extension attributes to use when parsing the CloudEvent. May be null.</param>
        /// <returns>A reference to a validated CloudEvent instance.</returns>
        public async static Task<CloudEvent> ToCloudEventAsync(this HttpListenerRequest httpListenerRequest,
            CloudEventFormatter formatter, IEnumerable<CloudEventAttribute> extensionAttributes) =>
            await ToCloudEventAsyncImpl(httpListenerRequest, formatter, extensionAttributes, async: true).ConfigureAwait(false);

        /// <summary>
        /// Converts this listener request into a CloudEvent object, with the given extension attributes.
        /// </summary>
        /// <param name="httpListenerRequest">The listener request to convert. Must not be null.</param>
        /// <param name="formatter">The event formatter to use to parse the CloudEvent. Must not be null.</param>
        /// <param name="extensionAttributes">The extension attributes to use when parsing the CloudEvent. May be null.</param>
        /// <returns>A reference to a validated CloudEvent instance.</returns>
        public static CloudEvent ToCloudEvent(this HttpListenerRequest httpListenerRequest,
            CloudEventFormatter formatter, params CloudEventAttribute[] extensionAttributes) =>
            ToCloudEvent(httpListenerRequest, formatter, (IEnumerable<CloudEventAttribute>) extensionAttributes);

        /// <summary>
        /// Converts this listener request into a CloudEvent object, with the given extension attributes.
        /// </summary>
        /// <param name="httpListenerRequest">The listener request to convert. Must not be null.</param>
        /// <param name="formatter">The event formatter to use to parse the CloudEvent. Must not be null.</param>
        /// <param name="extensionAttributes">The extension attributes to use when parsing the CloudEvent. May be null.</param>
        /// <returns>A reference to a validated CloudEvent instance.</returns>
        public static CloudEvent ToCloudEvent(this HttpListenerRequest httpListenerRequest,
            CloudEventFormatter formatter, IEnumerable<CloudEventAttribute> extensionAttributes) =>
            ToCloudEventAsyncImpl(httpListenerRequest, formatter, extensionAttributes, async: false).GetAwaiter().GetResult();

        private async static Task<CloudEvent> ToCloudEventAsyncImpl(HttpListenerRequest httpListenerRequest,
            CloudEventFormatter formatter, IEnumerable<CloudEventAttribute> extensionAttributes, bool async)
        {
            Validation.CheckNotNull(httpListenerRequest, nameof(httpListenerRequest));
            Validation.CheckNotNull(formatter, nameof(formatter));
            var stream = httpListenerRequest.InputStream;
            if (HasCloudEventsContentType(httpListenerRequest))
            {
                var contentType = MimeUtilities.CreateContentTypeOrNull(httpListenerRequest.ContentType);
                return async
                    ? await formatter.DecodeStructuredModeMessageAsync(stream, contentType, extensionAttributes).ConfigureAwait(false)
                    : formatter.DecodeStructuredModeMessage(stream, contentType, extensionAttributes);
            }
            else
            {
                string versionId = httpListenerRequest.Headers[HttpUtilities.SpecVersionHttpHeader];
                var version = CloudEventsSpecVersion.FromVersionId(versionId)
                    ?? throw new ArgumentException($"Unknown CloudEvents spec version '{versionId}'", nameof(httpListenerRequest));

                var cloudEvent = new CloudEvent(version, extensionAttributes);
                var headers = httpListenerRequest.Headers;
                foreach (var key in headers.AllKeys)
                {
                    string attributeName = HttpUtilities.GetAttributeNameFromHeaderName(key);
                    if (attributeName is null || attributeName == CloudEventsSpecVersion.SpecVersionAttribute.Name)
                    {
                        continue;
                    }
                    string attributeValue = HttpUtilities.DecodeHeaderValue(headers[key]);
                    cloudEvent.SetAttributeFromString(attributeName, attributeValue);
                }

                // The data content type should not have been set via a "ce-" header; instead,
                // it's in the regular content type.
                cloudEvent.DataContentType = httpListenerRequest.ContentType;

                ReadOnlyMemory<byte> data = async
                    ? await BinaryDataUtilities.ToReadOnlyMemoryAsync(stream).ConfigureAwait(false)
                    : BinaryDataUtilities.ToReadOnlyMemory(stream);
                formatter.DecodeBinaryModeEventData(data, cloudEvent);
                return Validation.CheckCloudEventArgument(cloudEvent, nameof(httpListenerRequest));
            }
        }

        private static bool HasCloudEventsContentType(HttpListenerRequest request) =>
            MimeUtilities.IsCloudEventsContentType(request.ContentType);

        private static bool HasCloudEventsBatchContentType(HttpListenerRequest request) =>
            MimeUtilities.IsCloudEventsBatchContentType(request.ContentType);
    }
}
