﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bjd;
using Bjd.net;
using Bjd.option;
using Bjd.sock;
using Bjd.util;
using BjdTest.test;
using NUnit.Framework;
using WebApiServer;

namespace WebApiServerTest{
    [TestFixture]
    public class MailTest : ILife{

        private static TmpOption _op; //設定ファイルの上書きと退避
        private static Server _v6Sv; //サーバ
        private static Server _v4Sv; //サーバ

        [TestFixtureSetUp]
        public static void BeforeClass(){

            //設定ファイルの退避と上書き
            _op = new TmpOption("WebApiServerTest", "WebApiServerTest.ini");
            Kernel kernel = new Kernel();
            var option = kernel.ListOption.Get("WebApi");
            Conf conf = new Conf(option);

            //サーバ起動
            _v4Sv = new Server(kernel, conf, new OneBind(new Ip(IpKind.V4Localhost), ProtocolKind.Tcp));
            _v4Sv.Start();

            _v6Sv = new Server(kernel, conf, new OneBind(new Ip(IpKind.V6Localhost), ProtocolKind.Tcp));
            _v6Sv.Start();

        }

        [TestFixtureTearDown]
        public static void AfterClass(){

            //サーバ停止
            _v4Sv.Stop();
            _v6Sv.Stop();

            _v4Sv.Dispose();
            _v6Sv.Dispose();

            //設定ファイルのリストア
            _op.Dispose();

        }

        [SetUp]
        public void SetUp(){
        }

        [TearDown]
        public void TearDown(){
        }
        
        //クライアントの生成
        SockTcp CreateClient(InetKind inetKind) {
            var port = 5050;
            if (inetKind == InetKind.V4){
                return Inet.Connect(new Kernel(), new Ip(IpKind.V4Localhost), port, 10, null);
            }
            return Inet.Connect(new Kernel(), new Ip(IpKind.V6Localhost), port, 10, null);
        }

        [TestCase(InetKind.V4)]
        [TestCase(InetKind.V6)]
        public void Test(InetKind inetKind) {

            //setUp
            var cl = CreateClient(inetKind);
            var expected = "TEST";

            //exercise
            cl.Send(Encoding.ASCII.GetBytes("GET /mail/message HTTP/1.0\n\n"));
            
            var buf = cl.Recv(3000,3, this);
            var actual = Encoding.UTF8.GetString(buf);
            //verify
            Assert.That(actual, Is.EqualTo(expected));

            //tearDown
            cl.Close();


        }

        public bool IsLife() {
            return true;
        }
    }
}
