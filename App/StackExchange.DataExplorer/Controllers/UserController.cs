﻿using System;
using System.Collections.Generic;
using System.Data.Linq;
using System.Linq;
using System.Web.Mvc;
using StackExchange.DataExplorer.Helpers;
using StackExchange.DataExplorer.Models;
using StackExchange.DataExplorer.ViewModel;

namespace StackExchange.DataExplorer.Controllers
{
    public class UserController : StackOverflowController
    {
        private static readonly HashSet<string> AllowedPreferences = new HashSet<string>
        {
            "HideSchema"
        };

        [Route("users")]
        public ActionResult Index(int? page)
        {
            int currentPage = Math.Max(page ?? 1, 1);

            SetHeader("Users");
            SelectMenuItem("Users");

            ViewData["PageNumbers"] = new PageNumber("/users?page=-1", Convert.ToInt32(Math.Ceiling(Current.DB.Users.Count() / 35m)), 50,
                                                     currentPage - 1, "pager fr");

            PagedList<User> data = Current.DB.Users.OrderBy(u => u.Login).ToPagedList(currentPage, 35);
            return View(data);
        }


        [ValidateInput(false)]
        [HttpPost]
        [Route(@"users/edit/{id:\d+}", RoutePriority.High)]
        public ActionResult Edit(int id, User updatedUser)
        {
            User user = Current.DB.Users.First(u => u.Id == id);

            if (updatedUser.DOB < DateTime.Now.AddYears(-100) || updatedUser.DOB > DateTime.Now.AddYears(-6))
            {
                updatedUser.DOB = null;
            }

            if (user.Id == updatedUser.Id && (updatedUser.Id == CurrentUser.Id || CurrentUser.IsAdmin))
            {
                var violations = updatedUser.GetBusinessRuleViolations(ChangeAction.Update);

                if (violations.Count == 0)
                {
                    user.Login = HtmlUtilities.Safe(updatedUser.Login);
                    user.AboutMe = updatedUser.AboutMe;
                    user.DOB = updatedUser.DOB;
                    user.Email = HtmlUtilities.Safe(updatedUser.Email);
                    user.Website = HtmlUtilities.Safe(updatedUser.Website);
                    user.Location = HtmlUtilities.Safe(updatedUser.Location);

                    Current.DB.SubmitChanges();

                    return Redirect("/users/" + user.Id);
                }
                else
                {
                    foreach (var violation in violations)
                        ModelState.AddModelError(violation.PropertyName, violation.ErrorMessage);

                    return Edit(user.Id);
                }
            }
            else
            {
                return Redirect("/");
            }
        }

        [HttpGet]
        [Route(@"users/edit/{id:\d+}", RoutePriority.High)]
        public ActionResult Edit(int id)
        {
            User user = Current.DB.Users.FirstOrDefault(u => u.Id == id);
            if (user == null)
            {
                return PageNotFound();
            }

            if (user.Id == CurrentUser.Id || CurrentUser.IsAdmin)
            {
                SetHeader(user.Login + " - Edit");
                SelectMenuItem("Users");

                return View(user);
            }
            else
            {
                return Redirect("/");
            }
        }

        [HttpPost]
        [Route(@"users/save-preference/{id:\d+}/{preference}")]
        public ActionResult SavePreference(int id, string preference, string value)
        {
            if (!AllowedPreferences.Contains(preference)) {
                return ContentError("Invalid preference");
            }

            User user = Current.DB.Users.FirstOrDefault(u => u.Id == id);

            if (user == null || (user.Id != CurrentUser.Id && !CurrentUser.IsAdmin))
            {
                return ContentError("Invalid action");
            }

            if (preference == "HideSchema")
            {
                user.HideSchema = value == "true";
            }

            Current.DB.SubmitChanges();

            return Content("ok");
        }

        [Route(@"users/{id:INT}/{name?}")]
        public ActionResult Show(int id, string name, string order_by)
        {
            User user = Current.DB.Users.FirstOrDefault(row => row.Id == id);
            if (user == null)
            {
                return PageNotFound();
            }
            // if this user has a display name, and the title is missing or does not match, permanently redirect to it
            if (user.UrlTitle.HasValue() && (string.IsNullOrEmpty(name) || name != user.UrlTitle))
            {
                return PageMovedPermanentlyTo(string.Format("/users/{0}/{1}",user.Id, HtmlUtilities.URLFriendly(user.Login)) + Request.Url.Query);
            }

            DBContext db = Current.DB;

            SetHeader(user.Login);
            SelectMenuItem("Users");

            order_by = order_by ?? "saved";

            ViewData["UserQueryHeaders"] = new SubHeader
            {
                Items = new List<SubHeaderViewData>
                {
                    new SubHeaderViewData
                    {
                        Description = "mine",
                        Title = "Queries you've worked on",
                        Href =
                            "/users/" + user.Id + "?order_by=mine",
                        Selected = (order_by == "saved")
                    },
                    new SubHeaderViewData
                    {
                        Description = "favorite",
                        Title = "Your favorite queries",
                        Href =
                            "/users/" + user.Id +
                            "?order_by=favorite",
                        Selected = (order_by == "favorite")
                    },
                    new SubHeaderViewData
                    {
                        Description = "recent",
                        Title = "Queries you've recently ran",
                        Href =
                            "/users/" + user.Id + "?order_by=recent",
                        Selected = (order_by == "recent")
                    }
                }
            };

            IEnumerable<QueryExecutionViewData> queries;

            if (order_by == "recent")
            {
                queries = Current.DB.Query<Metadata, QueryExecution, Site, Query, QueryExecutionViewData>(@"
                    SELECT
                        metadata.*, execution.*, site.*, query.*
                    FROM
                        QueryExecutions execution
                    JOIN
                        Revisions
                    ON
                        execution.RevisionId = Revisions.Id AND execution.UserId = @user
                    JOIN
                        Sites site
                    ON
                        site.Id = execution.SiteId
                    JOIN
                        Queries query
                    ON
                        query.Id = execution.QueryId
                    JOIN
                        Metadata metadata
                    ON
                        (
                            metadata.RevisionId = Revisions.RootId AND
                            metadata.OwnerId = Revisions.OwnerId
                        ) OR (
                            metadata.RevisionId = Revisions.Id AND
                            metadata.OwnerId = Revisions.OwnerId AND
                            Revisions.RootId IS NULL
                        ) OR (
                            metadata.RevisionId = Revisions.Id AND
                            metadata.OwnerId IS NULL AND
                            Revisions.OwnerId IS NULL
                        )
                    ORDER BY execution.LastRun DESC",
                    (metadata, execution, site, query) =>
                    {
                        return new QueryExecutionViewData
                        {
                            Id = execution.RevisionId,
                            Name = metadata.Title,
                            DefaultName = query.AsTitle(),
                            Description = metadata.Description,
                            FavoriteCount = metadata.Votes,
                            Views = metadata.Views,
                            LastRun = metadata.LastActivity,
                            Creator = user,
                            SiteName = Site.Name.ToLower(),
                            UseLatestLink = false
                        };
                    },
                    new
                    {
                        user = id
                    }
                );
            }
            else if (order_by == "favorite")
            {
                queries = Current.DB.Query<Metadata>(@"
                    SELECT
                        metadata.*
                    FROM
                        Votes
                    JOIN
                        Metadata metadata
                    ON
                        Votes.RootId = metadata.RevisionId AND
                        (
                            Votes.OwnerId = metadata.OwnerId OR
                            (Votes.OwnerId IS NULL AND metadata.OwnerID IS NULL)
                        ) AND
                        Votes.UserId = @user AND
                        Votes.VoteTypeId = @vote",
                    new
                    {
                        user = id,
                        vote = (int)VoteType.Favorite
                    }
                ).Select<Metadata, QueryExecutionViewData>(
                    (metadata) =>
                    {
                        return new QueryExecutionViewData
                        {
                            Id = metadata.RevisionId,
                            Name = metadata.Title,
                            // Figuring out the correct SQL-title here is an absolute pain,
                            // so either we need to store it as an updatable default in
                            // the metadata, or write the query to figure out which query
                            // we should be pulling
                            DefaultName = "unknown title",
                            Description = metadata.Description,
                            FavoriteCount = metadata.Votes,
                            Views = metadata.Views,
                            LastRun = metadata.LastActivity,
                            Creator = user,
                            SiteName = Site.Name.ToLower(),
                            UseLatestLink = true
                        };
                    }
                );
            }
            else
            {
                queries = Current.DB.Query<Metadata>(@"
                    SELECT
                        *
                    FROM
                        Metadata
                    WHERE
                        OwnerId = @user
                    ORDER BY
                        LastActivity",
                    new
                    {
                        user = id
                    }
                ).Select<Metadata, QueryExecutionViewData>(
                    (metadata) =>
                    {
                        return new QueryExecutionViewData
                        {
                            Id = metadata.RevisionId,
                            Name = metadata.Title,
                            // Same excuse as above
                            DefaultName = "unknown title",
                            Description = metadata.Description,
                            FavoriteCount = metadata.Votes,
                            Views = metadata.Views,
                            LastRun = metadata.LastActivity,
                            Creator = user,
                            SiteName = Site.Name.ToLower(),
                            UseLatestLink = true
                        };
                    }
                );
            }

            QueryExecutionViewData[] queriesArray = queries.ToArray();

            ViewData["Queries"] = queriesArray;

            if (queriesArray.Length == 0)
            {
                if (order_by == "recent")
                {
                    if (user.Id == CurrentUser.Id)
                    {
                        ViewData["EmptyMessage"] = "You have never ran any queries";
                    }
                    else
                    {
                        ViewData["EmptyMessage"] = "No queries ran recently";
                    }
                }
                else if (order_by == "favorite")
                {
                    if (user.Id == CurrentUser.Id)
                    {
                        ViewData["EmptyMessage"] =
                            "You have no favorite queries, click the star icon on a query to favorite it";
                    }
                    else
                    {
                        ViewData["EmptyMessage"] = "No favorites";
                    }
                }
                else
                {
                    if (user.Id == CurrentUser.Id)
                    {
                        ViewData["EmptyMessage"] = "You haven't edited any queries";
                    }
                    else
                    {
                        ViewData["EmptyMessage"] = "No queries";
                    }
                }
            }

            return View(user);
        }
    }
}