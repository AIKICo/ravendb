﻿// ------------------------------------------------------------[-----------
//  <copyright file="ChangesHandler.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Raven.Client.Extensions;
using Raven.Server.Routing;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Documents.Handlers
{
    public class ChangesHandler : DatabaseRequestHandler
    {
        private static readonly string StudioMarker = "fromStudio";

        [RavenAction("/databases/*/changes", "GET", AuthorizationStatus.ValidUser, EndpointType.Read, SkipUsagesCount = true, DisableOnCpuCreditsExhaustion = true)]
        public async Task GetChanges()
        {
            using (var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync())
            {
                using (ContextPool.AllocateOperationContext(out JsonOperationContext context))
                {
                    try
                    {
                        await HandleConnection(webSocket, context);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (TimeoutException e)
                    {
                        if (Logger.IsOperationsEnabled)
                            Logger.Operations("Timeout in changes handler", e);
                    }
                    catch (Exception ex)
                    {
                        if (Logger.IsInfoEnabled)
                            Logger.Info("Error encountered in changes handler", ex);

                        try
                        {
                            if (webSocket.State != WebSocketState.Open)
                                return;

                            await using (var ms = new MemoryStream())
                            {
                                await using (var writer = new AsyncBlittableJsonTextWriter(context, ms))
                                {
                                    context.Write(writer, new DynamicJsonValue
                                    {
                                        ["Type"] = "Error",
                                        ["Exception"] = ex.ToString()
                                    });
                                }

                                ms.TryGetBuffer(out ArraySegment<byte> bytes);
                                await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, Database.DatabaseShutdown);
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            // disposing
                        }
                        catch (Exception exception)
                        {
                            if (Logger.IsInfoEnabled)
                                Logger.Info("Failed to send the error in changes handler to the client", exception);
                        }
                    }
                }
            }
        }

        [RavenAction("/databases/*/changes/debug", "GET", AuthorizationStatus.ValidUser, EndpointType.Read)]
        public async Task GetConnectionsDebugInfo()
        {
            using (ContextPool.AllocateOperationContext(out JsonOperationContext context))
            await using (var writer = new AsyncBlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                writer.WriteStartObject();

                var connectionValues = Database.Changes.Connections.Values;

                writer.WritePropertyName("NumberOfConnections");
                writer.WriteInteger(connectionValues.Count);
                writer.WriteComma();

                writer.WritePropertyName("Connections");

                writer.WriteStartArray();
                var first = true;
                foreach (var connectionValue in connectionValues)
                {
                    if (first == false)
                        writer.WriteComma();
                    first = false;
                    context.Write(writer, connectionValue.GetDebugInfo());
                }
                writer.WriteEndArray();

                writer.WriteEndObject();
            }
        }

        private async Task HandleConnection(WebSocket webSocket, JsonOperationContext context)
        {
            var fromStudio = GetBoolValueQueryString(StudioMarker, false) ?? false;
            var throttleConnection = GetBoolValueQueryString("throttleConnection", false).GetValueOrDefault(false);

            var connection = new ChangesClientConnection(webSocket, Database, fromStudio);
            Database.Changes.Connect(connection);
            var sendTask = connection.StartSendingNotifications(throttleConnection);
            var debugTag = "changes/" + connection.Id;
            using (context.GetMemoryBuffer(out JsonOperationContext.MemoryBuffer segment1))
            using (context.GetMemoryBuffer(out JsonOperationContext.MemoryBuffer segment2))
            {
                try
                {
                    var segments = new[] { segment1, segment2 };
                    int index = 0;
                    var receiveAsync = webSocket.ReceiveAsync(segments[index].Memory.Memory, connection.CancellationToken.Token);
                    var jsonParserState = new JsonParserState();
                    using (var parser = new UnmanagedJsonParser(context, jsonParserState, debugTag))
                    {
                        connection.SendSupportedFeatures();

                        var result = await receiveAsync;
                        connection.CancellationToken.Token.ThrowIfCancellationRequested();

                        parser.SetBuffer(segments[index], 0, result.Count);
                        index++;
                        receiveAsync = webSocket.ReceiveAsync(segments[index].Memory.Memory, connection.CancellationToken.Token);

                        while (true)
                        {
                            using (var builder =
                                new BlittableJsonDocumentBuilder(context, BlittableJsonDocumentBuilder.UsageMode.None, debugTag, parser, jsonParserState))
                            {
                                parser.NewDocument();
                                builder.ReadObjectDocument();

                                while (builder.Read() == false)
                                {
                                    result = await receiveAsync;
                                    connection.CancellationToken.Token.ThrowIfCancellationRequested();

                                    parser.SetBuffer(segments[index], 0, result.Count);
                                    if (++index >= segments.Length)
                                        index = 0;
                                    receiveAsync = webSocket.ReceiveAsync(segments[index].Memory.Memory, connection.CancellationToken.Token);
                                }

                                builder.FinalizeDocument();

                                using (var reader = builder.CreateReader())
                                {
                                    if (reader.TryGet("Command", out string command) == false)
                                        throw new ArgumentNullException(nameof(command), "Command argument is mandatory");

                                    reader.TryGet("Param", out string commandParameter);
                                    reader.TryGet("Params", out BlittableJsonReaderArray commandParameters);

                                    connection.HandleCommand(command, commandParameter, commandParameters);

                                    if (reader.TryGet("CommandId", out int commandId))
                                    {
                                        connection.Confirm(commandId);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (IOException ex)
                {
                    /* Client was disconnected, write to log */
                    if (Logger.IsInfoEnabled)
                        Logger.Info("Client was disconnected", ex);
                }
                catch (Exception ex)
                {
#pragma warning disable 4014
                    sendTask.IgnoreUnobservedExceptions();
#pragma warning restore 4014

                    // if we received close from the client, we want to ignore it and close the websocket (dispose does it)
                    if (ex is WebSocketException webSocketException
                        && webSocketException.WebSocketErrorCode == WebSocketError.InvalidState
                        && webSocket.State == WebSocketState.CloseReceived)
                    {
                        // ignore
                    }
                    else if (ex is OperationCanceledException)
                    {
                        await sendTask; // will throw if the task is faulted
                        throw;
                    }
                    else
                    {
                        throw;
                    }
                }
                finally
                {
                    Database.Changes.Disconnect(connection.Id);
                }
            }

            Database.ThrowIfShutdownRequested();

            await sendTask;
        }

        [RavenAction("/databases/*/changes", "DELETE", AuthorizationStatus.ValidUser, EndpointType.Write)]
        public Task DeleteConnections()
        {
            var ids = GetStringValuesQueryString("id");

            foreach (var idStr in ids)
            {
                if (long.TryParse(idStr, NumberStyles.Any, CultureInfo.InvariantCulture, out long id) == false)
                    throw new ArgumentException($"Could not parse query string 'id' header as int64, value was: {idStr}");

                Database.Changes.Disconnect(id);
            }

            return NoContent();
        }
    }
}
