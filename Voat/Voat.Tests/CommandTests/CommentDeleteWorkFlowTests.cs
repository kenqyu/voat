﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Voat.Domain.Command;
using Voat.Domain.Query;

namespace Voat.Tests.CommandTests
{
    [TestClass]
    public class CommentDeleteWorkFlowTests
    {
        private static string AUTHOR_NAME = "TestUser13";
        private static string MOD_NAME = "unit";
        private static string CONTENT = Guid.NewGuid().ToString();
        private static int SUBMISSION_ID = 0;
        private static Tuple<int, int> CommentIDs;

        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {

            TestHelper.SetPrincipal(AUTHOR_NAME);

            //create submission
            var s = new CreateSubmissionCommand(new Domain.Models.UserSubmission() { Content = CONTENT, Title = "My Title Goes Here", Subverse = "unit" });
            var r1 = s.Execute().Result;
            Assert.IsTrue(r1.Success, r1.Message);
            SUBMISSION_ID = r1.Response.ID;

            //create user comment 1
            var c = new CreateCommentCommand(r1.Response.ID, null, CONTENT + "1");
            var rc = c.Execute().Result;
            Assert.IsTrue(rc.Success, rc.Message);

            //create user comment 2
            var c2 = new CreateCommentCommand(r1.Response.ID, null, CONTENT + "2");
            var rc2 = c2.Execute().Result;
            Assert.IsTrue(rc2.Success, rc2.Message);

            CommentIDs = Tuple.Create(rc.Response.ID, rc2.Response.ID);

            //Add comments to parents so segment gets returned.
            TestHelper.SetPrincipal(MOD_NAME);
            var d1 = new CreateCommentCommand(r1.Response.ID, CommentIDs.Item1, "Gunna so delete this");
            var rd2 = d1.Execute().Result;
            Assert.IsTrue(rd2.Success, rd2.Message);

            d1 = new CreateCommentCommand(r1.Response.ID, CommentIDs.Item2, "Gunna so delete this too!");
            rd2 = d1.Execute().Result;
            Assert.IsTrue(rd2.Success, rd2.Message);


        }
        [TestMethod]
        [TestCategory("Process"), TestCategory("Comment"), TestCategory("Command.Comment"), TestCategory("Query.Comment")]
        public void TestCommentDelete()
        {
            var authorCommentID = CommentIDs.Item1;
            var modCommentID = CommentIDs.Item2;

            //read content, ensure data is correct

            VerifyComment(authorCommentID, CONTENT, false);
            VerifyComment(modCommentID, CONTENT, false);

            //delete comment (mod)
            TestHelper.SetPrincipal(AUTHOR_NAME);
            var d = new DeleteCommentCommand(authorCommentID);
            var r = d.Execute().Result;
            Assert.IsTrue(r.Success, r.Message);

            TestHelper.SetPrincipal(MOD_NAME);
            d = new DeleteCommentCommand(modCommentID, "My Reason Here");
            r = d.Execute().Result;
            Assert.IsTrue(r.Success, r.Message);

            //read comment, ensure data exists
            VerifyComment(authorCommentID, "Deleted", true);
            VerifyComment(modCommentID, "Deleted", true);

        }
        private void VerifyComment(int commentID, string expectedContent, bool isDeleted)
        {
            var queryCommentDirect = new QueryComment(commentID, Caching.CachePolicy.None);
            var comment = queryCommentDirect.Execute();
            Assert.IsNotNull(comment);
            Assert.IsTrue(comment.Content.Contains(expectedContent));
            Assert.AreEqual(isDeleted, comment.IsDeleted);

            //purge cache to ensure fresh pull
            Caching.CacheHandler.Instance.Remove(Caching.CachingKey.CommentTree(comment.SubmissionID.Value));

            var queryCommentTree = new QueryCommentSegment(comment.SubmissionID.Value);
            var tree = queryCommentTree.Execute();

            var treeComment = tree.Comments.FirstOrDefault(x => x.ID == commentID);
            Assert.IsNotNull(treeComment);
            Assert.IsTrue(treeComment.Content.Contains(expectedContent));
            Assert.AreEqual(isDeleted, treeComment.IsDeleted);


        }

    }
}