﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.Collections.Generic;
using osu.Game.Online.Chat;

namespace osu.Game.Online.API.Requests
{
    public class GetMessagesRequest : APIRequest<List<Message>>
    {
        public readonly Channel Channel;

        public GetMessagesRequest(Channel channel)
        {
            Channel = channel;
        }

        protected override string Target => $@"chat/channels/{Channel.Id}/messages";
    }
}
