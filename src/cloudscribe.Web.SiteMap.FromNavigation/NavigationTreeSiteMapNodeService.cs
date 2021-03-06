﻿// Copyright (c) Source Tree Solutions, LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Author:                  Joe Audette
// Created:                 2016-04-20
// Last Modified:           2017-12-31
// 

using cloudscribe.Web.Navigation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace cloudscribe.Web.SiteMap
{
    /// <summary>
    /// for those of using cloudscribe.Web.Navigation, we already have this tree object that typically contains
    /// most or all the urls that we want to have indexed by search engines so rather than building a new list 
    /// it is more efficient to dual purpose this same data in order to build our sitemap.
    /// That is what this class is for.
    /// 
    /// blog items are typically not in a main navigation menu though so we typically also need a separate
    /// ISiteMapNodeService for blog posts
    /// </summary>
    public class NavigationTreeSiteMapNodeService : ISiteMapNodeService
    {
        public NavigationTreeSiteMapNodeService(
            NavigationTreeBuilderService siteMapTreeBuilder,
            IEnumerable<INavigationNodePermissionResolver> permissionResolvers,
            IUrlHelperFactory urlHelperFactory,
            IActionContextAccessor actionContextAccesor,
            IHttpContextAccessor contextAccessor,
            ILogger<NavigationTreeSiteMapNodeService> logger)
        {
            this.siteMapTreeBuilder = siteMapTreeBuilder;
            this.urlHelperFactory = urlHelperFactory;
            this.actionContextAccesor = actionContextAccesor;
            this.contextAccessor = contextAccessor;
            this.permissionResolvers = permissionResolvers;
            log = logger;
        }

        private NavigationTreeBuilderService siteMapTreeBuilder;
        private IUrlHelperFactory urlHelperFactory;
        private IActionContextAccessor actionContextAccesor;
        private ILogger log;
        private IHttpContextAccessor contextAccessor;
        private string baseUrl = string.Empty;
        private List<string> addedUrls = new List<string>();
        private IEnumerable<INavigationNodePermissionResolver> permissionResolvers;

        // this should not be needed in rc2 because there will be urlhelper methods for absolute url
        public string BaseUrl
        {
            get
            {
                if(string.IsNullOrEmpty(baseUrl))
                {
                    baseUrl = string.Concat(contextAccessor.HttpContext.Request.Scheme,
                        "://",
                        contextAccessor.HttpContext.Request.Host.ToUriComponent());
                }
                return baseUrl;
            }
        }

        private bool ShouldRenderNode(NavigationNode node)
        {
            TreeNode<NavigationNode> treeNode = new TreeNode<NavigationNode>(node);
            foreach(var permission in permissionResolvers)
            {
                bool ok = permission.ShouldAllowView(treeNode);
                if (!ok) return false;
            }

            return true;
        }


        public async Task<IEnumerable<ISiteMapNode>> GetSiteMapNodes(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var rootNode = await siteMapTreeBuilder.GetTree();
            var mapNodes = new List<SiteMapNode>();
            var urlHelper = urlHelperFactory.GetUrlHelper(actionContextAccesor.ActionContext);
            foreach (var navNode in rootNode.Flatten())
            {
                if (navNode.ExcludeFromSearchSiteMap) continue;

                if(ShouldRenderNode(navNode))
                {
                    var url = ResolveUrl(navNode, urlHelper);
                    if(string.IsNullOrEmpty(url))
                    {
                        log.LogWarning("failed to resolve url for node " + navNode.Key + ", skipping this node for sitemap");
                        continue;
                    }

                    if(!url.StartsWith("http"))
                    {
                        log.LogWarning("skipping relative url " + url + ", sitemap urls must be absolute");
                        continue;
                    }

                    if (addedUrls.Contains(url)) continue;

                    if(navNode is ISiteMapNode)
                    {
                        var smNode = navNode as ISiteMapNode;
                        mapNodes.Add(
                            new SiteMapNode(url)
                                {
                                    ChangeFrequency = smNode.ChangeFrequency,
                                    LastModified = smNode.LastModified,
                                    Priority = smNode.Priority
                                }
                            );
                    }
                    else
                    {
                        mapNodes.Add(new SiteMapNode(url));
                    }

                    addedUrls.Add(url);
                }
            }

            return mapNodes;

        }

        private string ResolveUrl(NavigationNode node, IUrlHelper urlHelper)
        {
            if (node.HideFromAnonymous) return string.Empty;

            // if url is already fully resolved just return it
            if (node.Url.StartsWith("http")) return node.Url;
            
            string urlToUse = string.Empty;
            if ((node.Action.Length > 0) && (node.Controller.Length > 0))
            {
                var a = node.Area == null ? "" : node.Area;
                urlToUse = urlHelper.Action(node.Action, node.Controller, new {area = a });
            }
            else if (node.NamedRoute.Length > 0)
            {
                urlToUse = urlHelper.RouteUrl(node.NamedRoute);
            }
            
            if (string.IsNullOrEmpty(urlToUse)) urlToUse = node.Url; 

            if (urlToUse.StartsWith("http")) return urlToUse;

            return BaseUrl + urlToUse;
        }

    }
}
