﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Bjd;
using Bjd.log;
using Bjd.mail;
using Bjd.net;
using Bjd.sock;
using Bjd.util;
using Debug = System.Diagnostics.Debug;

namespace SmtpServer {

    class OneFetchJob:LastError,IDisposable{
        private readonly OneFetch _oneFetch;//オプション
        private readonly Kernel _kernel;
        DateTime _dt = new DateTime(0);//最終処理時間
        private readonly int _timeout;
        private readonly int _sizeLimit;

        public OneFetchJob(Kernel kernel, OneFetch oneFetch, int timeout, int sizeLimit) {
            _kernel = kernel;
            _oneFetch = oneFetch;
            _timeout = timeout;
            _sizeLimit = sizeLimit;
        }

        //RETRの後のメールの保存が完成したら、Job2をこちらに乗せ換えられる
        public bool Job(Logger logger,DateTime now,ILife iLife){
            Debug.Assert(logger != null, "logger != null");

            var fetchDb = new FetchDb(_kernel.ProgDir(), _oneFetch.Name);
            var remoteUidList = new List<String>();
            var getList = new List<int>();//取得するメールのリスト
            var delList = new List<int>();//削除するメールのリスト

            //受信間隔を過ぎたかどうかの判断
            if (_dt.AddMinutes(_oneFetch.Interval) > now){
                return false;
            }

            logger.Set(LogKind.Normal, null, 1, _oneFetch.ToString());
            if (_oneFetch.Ip == null) {
                logger.Set(LogKind.Error, null, 2, "");
                return false;
            }
            var popClient = new PopClient(_oneFetch.Ip,_oneFetch.Port,_timeout,iLife);
            //接続
            if (!popClient.Connect()){
                logger.Set(LogKind.Error, null, 3, popClient.GetLastError());
                return false;
            }
            //ログイン
            if (!popClient.Login(_oneFetch.User, _oneFetch.Pass)){
                logger.Set(LogKind.Error, null, 4, popClient.GetLastError());
                return false;
            }
            //UID
            var lines = new List<String>();
            if (!popClient.Uidl(lines)) {
                logger.Set(LogKind.Error, null, 5, popClient.GetLastError());
                return false;
            }
            for (int i=0;i<lines.Count;i++){
                var tmp = lines[i].Split(' ');
                if (tmp.Length == 2){
                    var uid = tmp[1];
                    remoteUidList.Add(uid);

                    //既に受信が完了しているかどうかデータベースで確認する
                    if (fetchDb.IndexOf(uid) == -1) {
                        //存在しない場合
                        getList.Add(i);//受信対象とする
                    }
                }
            }
            if (_oneFetch.Synchronize == 0) { //サーバに残す
                for (var i = 0; i < remoteUidList.Count; i++) {
                    //保存期間が過ぎているかどうかを確認する
                    if (fetchDb.IsPast(remoteUidList[i], _oneFetch.KeepTime * 60)) { //サーバに残す時間（分）
                        delList.Add(i);
                    }
                }
            } else if (_oneFetch.Synchronize == 1) { //メールボックスと同期する
                //メールボックスの状態を取得する
                var localUidList = new List<string>();
                var folder = string.Format("{0}\\{1}", _kernel.MailBox.Dir, _oneFetch.LocalUser);
                foreach (var fileName in Directory.GetFiles(folder, "DF_*")) {
                    var mailInfo = new MailInfo(fileName);
                    localUidList.Add(mailInfo.Uid);
                }
                //メールボックスに存在しない場合、削除対象になる
                for (var i = 0; i < remoteUidList.Count; i++) {
                    if (localUidList.IndexOf(remoteUidList[i]) == -1) {
                        delList.Add(i);
                    }
                }
            } else if (_oneFetch.Synchronize == 2) { //サーバから削除
                //受信完了リストに存在する場合、削除対象になる
                for (var i = 0; i < remoteUidList.Count; i++) {
                    if (fetchDb.IndexOf(remoteUidList[i]) != -1) {
                        delList.Add(i);
                    }
                }
            }
            //RETR
            for (int i = 0; i < getList.Count;i++ ){
                var mail = new Mail();
                if (!popClient.Retr(getList[i], mail)) {
                    logger.Set(LogKind.Error, null, 6, popClient.GetLastError());
                    return false;
                }

                var from = new MailAddress(mail.GetHeader("From"));
                mail.ConvertHeader("X-UIDL", remoteUidList[i]);
                var remoteAddr = _oneFetch.Ip;
                var remoteHost = _oneFetch.Host;

                var rcptList = new List<MailAddress>();
//                rcptList.Add(new MailAddress(_oneFetch.LocalUser, server.DomainList[0]));
//                var error = false;
//                foreach (var to in server.Alias.Reflection(rcptList, logger)) {
//                    if (server.MailSave(@from, to, mail, remoteHost, remoteAddr))
//                        continue;
//                    error = true;
//                    break;
//                }
//                if (error) {
//                    break;
//                }


                fetchDb.Add(remoteUidList[i]);
            }
            //DELE
            for (int i = 0; i < delList.Count;i++ ){
                if (!popClient.Dele(delList[i])){
                    logger.Set(LogKind.Error, null, 7, popClient.GetLastError());
                    return false;
                }
                fetchDb.Del(remoteUidList[i]);
            }
            //QUIT
            if (!popClient.Quit()){
                logger.Set(LogKind.Error, null, 5, popClient.GetLastError());
                return false;
            }
            fetchDb.Save();
            _dt = DateTime.Now;//最終処理時刻の更新
            return true;
        }

        public void Job2(Server server,DateTime now,Logger logger,ILife iLife) {
            if (_dt.AddMinutes(_oneFetch.Interval) > now)//受信間隔を過ぎたかどうかの判断
                return;
            if (logger != null){
                logger.Set(LogKind.Normal, null, 23, _oneFetch.ToString());//###
            }
            Ssl ssl = null;
            Ip ip = _oneFetch.Ip;
            if (ip == null){
                //ERROR
            }
            int timeout = 3;
            var tcpObj = Inet.Connect(_kernel, ip, _oneFetch.Port, timeout, ssl);
            if (tcpObj == null) {
                if (logger != null){
                    logger.Set(LogKind.Error, null, 24, _oneFetch.ToString());//###
                }
                goto end;
            }
            Recv(server,tcpObj, logger, iLife);
        end:
            _dt = DateTime.Now;//最終処理時刻の更新
        }
        
        enum FetchState {
            User = 1,
            Pass = 2,
            Uidl = 3,
            Uidl2 = 4,
            Retr = 5,
            Retr2 = 6,
            Dele = 7,
            Job = 8,
            Quit = 9
        }

        void Recv(Server server,SockTcp sockTcp, Logger logger, ILife iLife) {
            var fetchDb = new FetchDb(_kernel.ProgDir(), _oneFetch.Name);
            var fetchState = FetchState.User;
            var remoteUidList = new List<string>();
            var getList = new List<int>();//取得するメールのリスト
            var delList = new List<int>();//削除するメールのリストs

            //ターゲット
            var n = 0;
            //メール受信バッファ
            var mail = new Mail();
            var delJob = false;//削除リスト生成が終わったかどうか（削除リストdelListはgetListの処理が全部終わってから作成される）

            while (iLife.IsLife()) {
                if (fetchState == FetchState.Retr2) { //Ver5.1.4 データ受信
                    //var lines = new List<byte[]>();//DATA受信バッファ
                    //if (!_server.RecvLines2(sockTcp, ref lines, _sizeLimit)) {
                    //    //DATA受信中にエラーが発生した場合は、直ちに切断する
                    //    Thread.Sleep(1000);
                    //    break;
                    //}
                    //受信が有効な場合
                    //foreach (byte[] line in lines) {
                    //    if (mail.Init(line)) {
                    //        //ヘッダ終了時の処理
                    //    }
                    //}

                    var data = new Data(_sizeLimit);
                    if (!data.Recv(sockTcp, 20, logger, iLife)) {
                        Thread.Sleep(1000);
                        break;
                    }

                    //以降は、RecvStatus.Successの場合
                    mail = data.Mail;


                    var from = new MailAddress(mail.GetHeader("From"));
                    //MailAddress to = new MailAddress(mail.GetHeader("To"));
                    //Ver5.6.0 string dateStr = mail.GetHeader("Date");
                    mail.ConvertHeader("X-UIDL", remoteUidList[n]);
                    var remoteAddr = sockTcp.RemoteIp;
                    var remoteHost = _kernel.DnsCache.GetHostName(remoteAddr.IPAddress, logger);

                    //Ver5.6.0 MailInfo mailInfo = new MailInfo(remoteUidList[n], mail.Length, remoteHost, remoteAddr, dateStr, from, to);
                    //Ver5.6.0 if (!kernel.MailBox.Save(oneFetch.LocalUser, mail, mailInfo))
                    //Ver5.6.0     break;
                    //Ver5.6.0
                    var rcptList = new List<MailAddress>();
                    rcptList.Add(new MailAddress(_oneFetch.LocalUser, server.DomainList[0]));
                    var error = false;
                    foreach (var to in server.Alias.Reflection(rcptList, logger)) {
                        if (server.MailSave(@from, to, mail, remoteHost, remoteAddr))
                            continue;
                        error = true;
                        break;
                    }
                    if (error) {
                        break;
                    }


                    fetchDb.Add(remoteUidList[n]);
                    fetchState = FetchState.Job;
                    //continue;
                } else {
                    //********************************************************************
                    // サーバからのレスポンス(+OK -ERR)受信
                    //********************************************************************
                    var data = sockTcp.LineRecv(_timeout, iLife);
                    if (data == null) //切断された
                        break;
                    if (data.Length == 0) {
                        Thread.Sleep(10);
                        continue;
                    }
                    //recvBuf = Inet.TrimCRLF(recvBuf);//\r\nの排除
                    var recvStr = Encoding.ASCII.GetString(data);
                    recvStr = Inet.TrimCrlf(recvStr);//\r\nの排除
                    //********************************************************************
                    // 受信したレスポンスコードによる処置
                    //********************************************************************
                    if (fetchState == FetchState.User || fetchState == FetchState.Pass || fetchState == FetchState.Uidl || fetchState == FetchState.Retr || fetchState == FetchState.Dele) {
                        if (recvStr.IndexOf("+OK") != 0)
                            break;//エラー発生
                    }
                    //********************************************************************
                    // 各モードごとの動作
                    //********************************************************************
                    if (fetchState == FetchState.User) {
                        sockTcp.AsciiSend(string.Format("USER {0}", _oneFetch.User));
                        fetchState = FetchState.Pass;
                    } else if (fetchState == FetchState.Pass) {
                        sockTcp.AsciiSend(string.Format("PASS {0}", _oneFetch.Pass));
                        fetchState = FetchState.Uidl;
                    } else if (fetchState == FetchState.Uidl) {
                        sockTcp.AsciiSend("UIDL");
                        n = 0;
                        fetchState = FetchState.Uidl2;
                    } else if (fetchState == FetchState.Retr) {
                        fetchState = FetchState.Retr2;
                    } else if (fetchState == FetchState.Dele) {
                        fetchDb.Del(remoteUidList[n]);
                        fetchState = FetchState.Job;
                    } else if (fetchState == FetchState.Uidl2) {
                        if (recvStr.IndexOf("+OK") != 0) {
                            if (recvStr == ".") {
                                fetchState = FetchState.Job;
                            } else {
                                var tmp = recvStr.Split(' ');
                                if (tmp.Length != 2)
                                    break;
                                var uid = tmp[1];

                                remoteUidList.Add(uid);
                                //既に受信が完了しているかどうかデータベースで確認する
                                if (fetchDb.IndexOf(uid) == -1) {
                                    //存在しない場合
                                    getList.Add(n);//受信対象とする
                                }
                                n++;
                            }
                        }
                        //} else if (mode == MODE.RETR2) {
                        //    if (recvStr == ".") {
                        //        MailAddress from = new MailAddress(mail.GetHeader("From"));
                        //        MailAddress to = new MailAddress(mail.GetHeader("To"));
                        //        string dateStr = mail.GetHeader("Date");
                        //        mail.ConvertHeader("X-UIDL",remoteUidList[n]);
                        //        Ip remoteAddr = new Ip(sockTcp.RemoteEndPoint.Address.ToString());
                        //        string remoteHost = kernel.dnsCache.Get(remoteAddr.IPAddress);
                        //        MailInfo mailInfo = new MailInfo(remoteUidList[n],mail.Length,remoteHost,remoteAddr,dateStr,from,to);
                        //        if (!kernel.MailBox.Save(localUser,mail,mailInfo))
                        //            break;
                        //        fetchDb.Add(remoteUidList[n]);
                        //        mode = MODE.JOB;
                        //    } else {
                        //        mail.Init(data);
                        //    }
                    } else if (fetchState == FetchState.Quit) {
                        break;
                    }
                }
                if (fetchState == FetchState.Job) {
                    if (getList.Count == 0 && !delJob) { //削除リスト生成がまだ処理されていない場合
                        delJob = true;
                        if (_oneFetch.Synchronize == 0) { //サーバに残す
                            for (var i = 0; i < remoteUidList.Count; i++) {
                                //保存期間が過ぎているかどうかを確認する
                                if (fetchDb.IsPast(remoteUidList[i], _oneFetch.KeepTime*60)) { //サーバに残す時間（分）
                                    delList.Add(i);
                                }
                            }
                        } else if (_oneFetch.Synchronize == 1) { //メールボックスと同期する
                            //メールボックスの状態を取得する
                            var localUidList = new List<string>();
                            var folder = string.Format("{0}\\{1}", _kernel.MailBox.Dir, _oneFetch.LocalUser);
                            foreach (var fileName in Directory.GetFiles(folder, "DF_*")) {
                                var mailInfo = new MailInfo(fileName);
                                localUidList.Add(mailInfo.Uid);
                            }
                            //メールボックスに存在しない場合、削除対象になる
                            for (var i = 0; i < remoteUidList.Count; i++) {
                                if (localUidList.IndexOf(remoteUidList[i]) == -1) {
                                    delList.Add(i);
                                }
                            }
                        } else if (_oneFetch.Synchronize == 2) { //サーバから削除
                            //受信完了リストに存在する場合、削除対象になる
                            for (var i = 0; i < remoteUidList.Count; i++) {
                                if (fetchDb.IndexOf(remoteUidList[i]) != -1) {
                                    delList.Add(i);
                                }
                            }
                        }
                    }

                    //getListが存在する場合
                    if (getList.Count > 0) {
                        n = getList[0];
                        getList.RemoveAt(0);
                        sockTcp.AsciiSend(string.Format("RETR {0}", n + 1));
                        mail = new Mail();
                        fetchState = FetchState.Retr;
                    } else if (delList.Count > 0) {
                        n = delList[0];
                        delList.RemoveAt(0);
                        sockTcp.AsciiSend(string.Format("DELE {0}", n + 1));
                        fetchState = FetchState.Dele;
                    } else {
                        sockTcp.AsciiSend("QUIT");
                        fetchState = FetchState.Quit;
                    }
                }
            }
            fetchDb.Save();
        }
        public void Dispose(){
            ;
        }
    }
}