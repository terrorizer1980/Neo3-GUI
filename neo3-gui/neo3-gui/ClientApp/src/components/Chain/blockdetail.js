/* eslint-disable */ 
//just test replace wallet//
import React from 'react';
import {Link} from 'react-router-dom';
import { Layout, Row, Col, message } from 'antd';
import axios from 'axios';
import Intitle from '../Common/intitle';
import Transaction from '../Transaction/transaction';

const { Content } = Layout;

class Blockdetail extends React.Component{
  constructor(props){
    super(props);
    this.state = {
      blockdetail: {},
      height:0,
      witness:"",
      nonce:0
    };
  }
  componentDidMount(){
    let _h = Number(location.pathname.split(":")[1])
    this.setHeight(_h)();
  }
  getAllblock = () =>{
    var _this = this;
    let _height = this.state.height;
    axios.post('http://localhost:8081', {
        "id":"1111",
        "method": "GetBlock",
        "params": {
            "index": _height
        }
    })
    .then(function (response) {
      var _data = response.data;
      console.log(_data);
      if(_data.msgType === -1){
        message.error("查询失败,该高度错误");
        return;
      }
      _this.setState({
        blockdetail:_data.result
      })
      _this.setState({
        witness:_data.result.witness.scriptHash
      })
      _this.setState({
        nonce:_data.result.consensusData.nonce
      })
    })
    .catch(function (error) {
      console.log(error);
      console.log("error");
    });
  }
  setHeight = (h) => {
    return () =>{
        this.setState({
            height: h
        },() => this.getAllblock());
    }
  }
  render(){
    const {blockdetail,witness,nonce} = this.state;
    return (
      <Layout className="gui-container">
          <Content className="mt3">
          <Row gutter={[30, 0]} type="flex">
            <Col span={24} className="bg-white pv4">
              <Intitle className="mb2" content="区块信息"/>
              <div className="info-detail pa3 pv3">
                <Link to={"/chain/detail:" + blockdetail.blockHeight}><span>Hash: &nbsp;&nbsp;&nbsp;</span>{blockdetail.blockHash}</Link>
                <Row>
                    <Col span={12}>
                        <ul className="detail-ul">
                            <li><span>高度：</span>{blockdetail.blockHeight}</li>
                            <li><span>时间戳：</span>{blockdetail.blockTime}</li>
                            <li><span>网络费：</span>{blockdetail.networkFee?blockdetail.networkFee:'--'}</li>
                            <li><span>确认数：</span>{blockdetail.confirmations}</li>
                            <li><span>上一区块：</span><Link to={"/chain/detail:" + (blockdetail.blockHeight-1)} onClick={this.setHeight(blockdetail.blockHeight-1)}>{blockdetail.blockHeight-1}</Link></li>
                        </ul>
                    </Col>
                    <Col span={12}>
                        <ul className="detail-ul">
                            <li><span>大小：</span>{blockdetail.blockHeight}</li>
                            <li><span>随机数：</span>{nonce}</li>
                            <li><span>系统费：</span>{blockdetail.networkFee?blockdetail.networkFee:'--'}</li>
                            <li><span>见证人：</span>{witness}</li>
                            <li><span>下一区块：</span><Link to={"/chain/detail:" + (blockdetail.blockHeight+1)} onClick={this.setHeight(blockdetail.blockHeight+1)}>{blockdetail.blockHeight+1}</Link></li>
                        </ul>
                    </Col>
                </Row>
              </div>
            </Col>
          </Row>
          <Transaction content="交易列表" />
        </Content>
      </Layout>
    );
  }
} 

export default Blockdetail;