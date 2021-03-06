﻿using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using ServiceStack;
using System;
using System.Linq;

namespace MediaBrowser.Api
{
    [Route("/Videos/{Id}/AdditionalParts", "GET")]
    [Api(Description = "Gets additional parts for a video.")]
    public class GetAdditionalParts : IReturn<ItemsResult>
    {
        [ApiMember(Name = "UserId", Description = "Optional. Filter by user id, and attach user data", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
        public Guid? UserId { get; set; }

        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        /// <value>The id.</value>
        [ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string Id { get; set; }
    }

    [Route("/Videos/{Id}/AlternateVersions", "GET")]
    [Api(Description = "Gets alternate versions of a video.")]
    public class GetAlternateVersions : IReturn<ItemsResult>
    {
        [ApiMember(Name = "UserId", Description = "Optional. Filter by user id, and attach user data", IsRequired = false, DataType = "string", ParameterType = "query", Verb = "GET")]
        public Guid? UserId { get; set; }

        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        /// <value>The id.</value>
        [ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string Id { get; set; }
    }

    [Route("/Videos/{Id}/AlternateVersions", "POST")]
    [Api(Description = "Assigns videos as alternates of antoher.")]
    public class PostAlternateVersions : IReturnVoid
    {
        [ApiMember(Name = "AlternateVersionIds", Description = "Item id, comma delimited", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "POST")]
        public string AlternateVersionIds { get; set; }

        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        /// <value>The id.</value>
        [ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string Id { get; set; }
    }

    [Route("/Videos/{Id}/AlternateVersions", "DELETE")]
    [Api(Description = "Assigns videos as alternates of antoher.")]
    public class DeleteAlternateVersions : IReturnVoid
    {
        [ApiMember(Name = "AlternateVersionIds", Description = "Item id, comma delimited", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "DELETE")]
        public string AlternateVersionIds { get; set; }

        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        /// <value>The id.</value>
        [ApiMember(Name = "Id", Description = "Item Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string Id { get; set; }
    }

    public class VideosService : BaseApiService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IDtoService _dtoService;

        public VideosService(ILibraryManager libraryManager, IUserManager userManager, IDtoService dtoService)
        {
            _libraryManager = libraryManager;
            _userManager = userManager;
            _dtoService = dtoService;
        }

        /// <summary>
        /// Gets the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>System.Object.</returns>
        public object Get(GetAdditionalParts request)
        {
            var user = request.UserId.HasValue ? _userManager.GetUserById(request.UserId.Value) : null;

            var item = string.IsNullOrEmpty(request.Id)
                           ? (request.UserId.HasValue
                                  ? user.RootFolder
                                  : _libraryManager.RootFolder)
                           : _dtoService.GetItemByDtoId(request.Id, request.UserId);

            // Get everything
            var fields = Enum.GetNames(typeof(ItemFields))
                    .Select(i => (ItemFields)Enum.Parse(typeof(ItemFields), i, true))
                    .ToList();

            var video = (Video)item;

            var items = video.GetAdditionalParts()
                         .Select(i => _dtoService.GetBaseItemDto(i, fields, user, video))
                         .ToArray();

            var result = new ItemsResult
            {
                Items = items,
                TotalRecordCount = items.Length
            };

            return ToOptimizedSerializedResultUsingCache(result);
        }

        public object Get(GetAlternateVersions request)
        {
            var user = request.UserId.HasValue ? _userManager.GetUserById(request.UserId.Value) : null;

            var item = string.IsNullOrEmpty(request.Id)
                           ? (request.UserId.HasValue
                                  ? user.RootFolder
                                  : _libraryManager.RootFolder)
                           : _dtoService.GetItemByDtoId(request.Id, request.UserId);

            // Get everything
            var fields = Enum.GetNames(typeof(ItemFields))
                    .Select(i => (ItemFields)Enum.Parse(typeof(ItemFields), i, true))
                    .ToList();

            var video = (Video)item;

            var items = video.GetAlternateVersions()
                         .Select(i => _dtoService.GetBaseItemDto(i, fields, user, video))
                         .ToArray();

            var result = new ItemsResult
            {
                Items = items,
                TotalRecordCount = items.Length
            };

            return ToOptimizedSerializedResultUsingCache(result);
        }

        public void Post(PostAlternateVersions request)
        {
            var task = AddAlternateVersions(request);

            Task.WaitAll(task);
        }

        public void Delete(DeleteAlternateVersions request)
        {
            var task = RemoveAlternateVersions(request);

            Task.WaitAll(task);
        }

        private async Task AddAlternateVersions(PostAlternateVersions request)
        {
            var video = (Video)_dtoService.GetItemByDtoId(request.Id);

            var list = new List<LinkedChild>();
            var currentAlternateVersions = video.GetAlternateVersions().ToList();

            foreach (var itemId in request.AlternateVersionIds.Split(',').Select(i => new Guid(i)))
            {
                var item = _libraryManager.GetItemById(itemId) as Video;

                if (item == null)
                {
                    throw new ArgumentException("No item exists with the supplied Id");
                }

                if (currentAlternateVersions.Any(i => i.Id == itemId))
                {
                    throw new ArgumentException("Item already exists.");
                }

                list.Add(new LinkedChild
                {
                    Path = item.Path,
                    Type = LinkedChildType.Manual
                });

                item.PrimaryVersionId = video.Id;
            }

            video.LinkedAlternateVersions.AddRange(list);

            await video.UpdateToRepository(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);

            await video.RefreshMetadata(CancellationToken.None).ConfigureAwait(false);
        }

        private async Task RemoveAlternateVersions(DeleteAlternateVersions request)
        {
            var video = (Video)_dtoService.GetItemByDtoId(request.Id);
        }
    }
}
