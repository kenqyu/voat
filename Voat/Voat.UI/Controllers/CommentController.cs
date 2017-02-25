﻿/*
This source file is subject to version 3 of the GPL license, 
that is bundled with this package in the file LICENSE, and is 
available online at http://www.gnu.org/licenses/gpl.txt; 
you may not use this file except in compliance with the License. 

Software distributed under the License is distributed on an "AS IS" basis,
WITHOUT WARRANTY OF ANY KIND, either express or implied. See the License for
the specific language governing rights and limitations under the License.

All portions of the code written by Voat are Copyright (c) 2015 Voat, Inc.
All Rights Reserved.
*/

//using Microsoft.AspNet.SignalR;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Mvc;
using Voat.Caching;
using Voat.Configuration;
using Voat.Data;
using Voat.Data.Models;
using Voat.Domain;
using Voat.Domain.Command;
using Voat.Domain.Models;
using Voat.Domain.Query;
using Voat.Models;
using Voat.Models.ViewModels;
using Voat.UI.Utilities;
using Voat.Utilities;
using Voat.Utilities.Components;

namespace Voat.Controllers
{
    public class CommentController : BaseController
    {
        // POST: votecomment/{commentId}/{typeOfVote}
        [HttpPost]
        [Authorize]
        [VoatValidateAntiForgeryToken]
        public async Task<JsonResult> VoteComment(int commentId, int typeOfVote)
        {
            var cmd = new CommentVoteCommand(commentId, typeOfVote, IpHash.CreateHash(UserHelper.UserIpAddress(this.Request)));
            var result = await cmd.Execute();
            return Json(result);
        }

       

        // GET: Renders Primary Submission Comments Page
        public async Task<ActionResult> Comments(int? submissionID, string subverseName, int? commentID, string sort, int? context)
        {
            #region Validation

            if (submissionID == null)
            {
                return GenericErrorView(new ErrorViewModel() { Description = "Can not find what was requested because input is not valid" });
            }

            var q = new QuerySubmission(submissionID.Value, true);

            var submission = await q.ExecuteAsync().ConfigureAwait(false);

            if (submission == null)
            {
                return NotFoundErrorView();
            }

            // make sure that the combination of selected subverse and submission subverse are linked
            if (!submission.Subverse.Equals(subverseName, StringComparison.OrdinalIgnoreCase))
            {
                return NotFoundErrorView();
            }

            var subverse = DataCache.Subverse.Retrieve(subverseName);

            if (subverse == null)
            {
                return NotFoundErrorView();
            }

            if (subverse.IsAdminDisabled.HasValue && subverse.IsAdminDisabled.Value)
            {
                ViewBag.Subverse = subverse.Name;
                return SubverseDisabledErrorView();
            }

            #endregion

            if (commentID != null)
            {
                ViewBag.StartingCommentId = commentID;
                ViewBag.CommentToHighLight = commentID;
            }

            #region Set ViewBag
            ViewBag.Subverse = subverse;
            ViewBag.Submission = submission;
            //This is a required view bag property in _Layout.cshtml
            ViewBag.SelectedSubverse = subverse.Name;

            var SortingMode = (sort == null ? "top" : sort).ToLower();
            ViewBag.SortingMode = SortingMode;

            #endregion

            var cmd = new LogVisitCommand(submissionID.Value, UserHelper.UserIpAddress(this.Request));
            cmd.Execute();

            CommentSegment model = null;
            if (commentID != null)
            {
                ViewBag.CommentToHighLight = commentID.Value;
                model = await GetCommentContext(submission.ID, commentID.Value, context, sort);
            }
            else
            {
                model = await GetCommentSegment(submission.ID, null, 0, sort);
            }

            var q2 = new QuerySubverseModerators(subverseName);
            ViewBag.ModeratorList = await q2.ExecuteAsync();



            ViewBag.NavigationViewModel = new NavigationViewModel()
            {
                Description = "Subverse",
                Name = subverseName,
                MenuType = MenuType.Subverse,
                BasePath = VoatPathHelper.BasePath(new DomainReference(DomainType.Subverse, subverseName)),
                Sort = null
            };


            return View("~/Views/Home/Comments.cshtml", model);

        }

        private async Task<CommentSegment> GetCommentSegment(int submissionID, int? parentID, int startingIndex, string sort)
        {
            //attempt to parse sort
            var sortAlg = CommentSortAlgorithm.Top;
            if (!Enum.TryParse(sort, true, out sortAlg))
            {
                sortAlg = CommentSortAlgorithm.Top;
            }

            var q = new QueryCommentSegment(submissionID, parentID, startingIndex, sortAlg);
            var results = await q.ExecuteAsync();
            return results;
        }
        private async Task<CommentSegment> GetCommentContext(int submissionID, int commentID, int? contextCount, string sort)
        {
            //attempt to parse sort
            var sortAlg = CommentSortAlgorithm.Top;
            if (!Enum.TryParse(sort, true, out sortAlg))
            {
                sortAlg = CommentSortAlgorithm.Top;
            }
            var q = new QueryCommentContext(submissionID, commentID, contextCount, sortAlg);
            var results = await q.ExecuteAsync();
            return results;
        }
        // url: "/comments/" + submission + "/" + parentId + "/" + command + "/" + startingIndex + "/" + count + "/" + nestingLevel + "/" + sort + "/",
        // GET: Renders a Section of Comments within the already existing tree
        //Leaving (string command) in for backwards compat with mobile html clients. this is no longer used
        public async Task<ActionResult> CommentSegment(int submissionID, int? parentID, string command, int startingIndex, string sort)
        {
            #region Validation

            if (submissionID <= 0)
            {
                return GenericErrorView(new ErrorViewModel() { Description = "Can not find what was requested because input is not valid" });
            }

            var q = new QuerySubmission(submissionID, false);
            var submission = await q.ExecuteAsync().ConfigureAwait(false);

            if (submission == null)
            {
                return NotFoundErrorView();
            }

            var subverse = DataCache.Subverse.Retrieve(submission.Subverse);

            if (subverse == null)
            {
                return NotFoundErrorView();
            }

            if (subverse.IsAdminDisabled.HasValue && subverse.IsAdminDisabled.Value)
            {
                ViewBag.Subverse = subverse.Name;
                return SubverseDisabledErrorView();
            }

            #endregion

            #region Set ViewBag
            ViewBag.Subverse = subverse;
            ViewBag.Submission = submission;

            var SortingMode = (sort == null ? "top" : sort).ToLower();
            ViewBag.SortingMode = SortingMode;

            #endregion

            var q2 = new QuerySubverseModerators(subverse.Name);
            ViewBag.ModeratorList = await q2.ExecuteAsync();

            var results = await GetCommentSegment(submissionID, parentID, startingIndex, sort);
            return PartialView("~/Views/Shared/Comments/_CommentSegment.cshtml", results);
        }

        // GET: Renders a New Comment Tree
        public async Task<ActionResult> CommentTree(int submissionID, string sort)
        {
            #region Validation

            if (submissionID <= 0)
            {
                return GenericErrorView(new ErrorViewModel() { Description = "Can not find what was requested because input is not valid" });
            }

            var submission = DataCache.Submission.Retrieve(submissionID);

            if (submission == null)
            {
                return NotFoundErrorView();
            }

            var subverse = DataCache.Subverse.Retrieve(submission.Subverse);

            if (subverse == null)
            {
                return NotFoundErrorView();
            }

            if (subverse.IsAdminDisabled.HasValue && subverse.IsAdminDisabled.Value)
            {
                ViewBag.Subverse = subverse.Name;
                return SubverseDisabledErrorView();
            }

            #endregion

            #region Set ViewBag
            ViewBag.Subverse = subverse;
            ViewBag.Submission = submission;

            var SortingMode = (sort == null ? "top" : sort).ToLower();
            ViewBag.SortingMode = SortingMode;

            #endregion

            var results = await GetCommentSegment(submissionID, null, 0, sort);
            return PartialView("~/Views/Shared/Comments/_CommentTree.cshtml", results);
        }

        // GET: submitcomment
        public ActionResult SubmitComment()
        {
            return NotFoundErrorView();
        }

        // POST: submitcomment, adds a new root comment
        [HttpPost]
        [Authorize]
        [PreventSpam(DelayRequest = 15, ErrorMessage = "Sorry, you are doing that too fast. Please try again later.")]
        [VoatValidateAntiForgeryToken]
        public async Task<ActionResult> SubmitComment([Bind(Include = "ID, Content, SubmissionID, ParentID")] Data.Models.Comment commentModel)
        {

            //return JsonError("This is an error message");

            if (!ModelState.IsValid)
            {
                //Model isn't valid, can include throttling
                if (Request.IsAjaxRequest())
                {
                    return JsonError(ModelState.GetFirstErrorMessage());
                }
                else
                {
                    ModelState.AddModelError(String.Empty, "Sorry, you are either banned from this sub or doing that too fast. Please try again in 2 minutes.");
                    return View("~/Views/Help/SpeedyGonzales.cshtml");
                }
            }
            else
            {
                var cmd = new CreateCommentCommand(commentModel.SubmissionID.Value, commentModel.ParentID, commentModel.Content);
                var result = await cmd.Execute();

                if (result.Success)
                {
                    //if good return formatted comment
                    if (Request.IsAjaxRequest())
                    {
                        var comment = result.Response;
                        comment.IsOwner = true;
                        ViewBag.CommentId = comment.ID; //why?
                        ViewBag.rootComment = comment.ParentID == null; //why?
                        return PartialView("~/Views/Shared/Comments/_SubmissionComment.cshtml", comment);
                    }
                    else if (Request.UrlReferrer != null)
                    {
                        var url = Request.UrlReferrer.AbsolutePath;
                        return Redirect(url);
                    }
                    else
                    {
                        return new EmptyResult();
                    }
                }
                else
                {
                    return JsonError(result.Message);
                }
            }
        }

        // POST: editcomment
        [HttpPost]
        [Authorize]
        [VoatValidateAntiForgeryToken]
        [PreventSpam(DelayRequest = 15, ErrorMessage = "Sorry, you are doing that too fast. Please try again later.")]
        public async Task<ActionResult> EditComment([Bind(Include = "ID, Content")] Data.Models.Comment commentModel)
        {
            if (ModelState.IsValid)
            {
                var cmd = new EditCommentCommand(commentModel.ID, commentModel.Content);
                var result = await cmd.Execute();

                if (result.Success)
                {
                    return Json(new { response = result.Response.FormattedContent });
                }
                else
                {
                    return JsonError(result.Message);
                }
            }
            else
            {
                return JsonError(ModelState.GetFirstErrorMessage());
            }
        }
        // POST: deletecomment
        [HttpPost]
        [Authorize]
        [VoatValidateAntiForgeryToken]
        public async Task<ActionResult> DeleteComment(int commentId)
        {
            if (ModelState.IsValid)
            {
                var cmd = new DeleteCommentCommand(commentId, "This feature is not yet implemented");
                var result = await cmd.Execute();

                if (result.Success)
                {
                    if (Request.IsAjaxRequest())
                    {
                        return new HttpStatusCodeResult(HttpStatusCode.OK);
                    }
                    var url = Request.UrlReferrer.AbsolutePath;
                    return Redirect(url);
                }
                else
                {
                    return new HttpStatusCodeResult(HttpStatusCode.BadRequest, result.Message);
                }
            }
            else
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
        }

        #region MOVE TO SubverseModeration CONTROLLER

        // POST: comments/distinguish/{commentId}
        [Authorize]
        public async Task<JsonResult> DistinguishComment(int commentId)
        {
            using (var _db = new voatEntities())
            {
                var commentToDistinguish = _db.Comments.Find(commentId);

                if (commentToDistinguish != null)
                {
                    // check to see if request came from comment author
                    if (User.Identity.Name == commentToDistinguish.UserName)
                    {
                        // check to see if comment author is also sub mod or sub admin for comment sub
                        if (ModeratorPermission.HasPermission(User.Identity.Name, commentToDistinguish.Submission.Subverse, ModeratorAction.DistinguishContent))
                        {
                            // mark the comment as distinguished and save to db
                            if (commentToDistinguish.IsDistinguished)
                            {
                                commentToDistinguish.IsDistinguished = false;
                            }
                            else
                            {
                                commentToDistinguish.IsDistinguished = true;
                            }

                            await _db.SaveChangesAsync();

                            //Update Cache
                            CacheHandler.Instance.DictionaryReplace<int, usp_CommentTree_Result>(CachingKey.CommentTree(commentToDistinguish.SubmissionID.Value), commentToDistinguish.ID, x => { x.IsDistinguished = commentToDistinguish.IsDistinguished; return x; }, true);

                            Response.StatusCode = 200;
                            return Json("Distinguish flag changed.", JsonRequestBehavior.AllowGet);
                        }
                    }
                }
            }

            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return Json("Unauthorized distinguish attempt.", JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [Authorize]
        public async Task<ActionResult> ModeratorDelete(string subverse, int submissionID, int commentID)
        {

            if (!ModeratorPermission.HasPermission(User.Identity.Name, subverse, Domain.Models.ModeratorAction.DeleteComments))
            {
                return new HttpUnauthorizedResult();
            }
            var q = new QueryComment(commentID);
            var comment = await q.ExecuteAsync();

            if (comment == null || comment.SubmissionID != submissionID)
            {
                ModelState.AddModelError("", "Can not find comment. Who did this?");
                return View(new ModeratorDeleteContentViewModel());
            }
            ViewBag.Comment = comment;
            return View(new ModeratorDeleteContentViewModel() { ID = commentID });
        }

        [HttpPost]
        [Authorize]
        [VoatValidateAntiForgeryToken]
        public async Task<ActionResult> ModeratorDelete(string subverse, int submissionID, ModeratorDeleteContentViewModel model)
        {
            var q = new QueryComment(model.ID);
            var comment = await q.ExecuteAsync();

            if (comment == null || comment.SubmissionID != submissionID)
            {
                ModelState.AddModelError("", "Can not find comment. Who did this?");
                return View(new ModeratorDeleteContentViewModel());
            }

            if (!ModeratorPermission.HasPermission(User.Identity.Name, comment.Subverse, Domain.Models.ModeratorAction.DeleteComments))
            {
                return new HttpUnauthorizedResult();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var cmd = new DeleteCommentCommand(model.ID, model.Reason);
            var r = await cmd.Execute();
            if (r.Success)
            {
                     return RedirectToRoute("SubverseCommentsWithSort_Short", new { subverseName = subverse, submissionID = submissionID });
            }
            else
            {
                ModelState.AddModelError("", r.Message);
                return View(model);
            }




        }

        #endregion 
    }
}