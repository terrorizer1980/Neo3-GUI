import React from 'react';
import { Layout } from 'antd';
import Sync from '../sync';
import Transaction from '../Transaction/transaction';
import Untransaction from '../Transaction/untransaction';

class Wallettrans extends React.Component{
  constructor(props){
    super(props);
    this.state = {
        size: 'default',
        translist:[],
    };
  }
  render = () =>{
    return (
      <Layout className="gui-container">
        <Sync />
        <Untransaction />
        <Transaction  content={"交易记录"} page="wallettrans"/>
      </Layout>
    );
  }
} 

export default Wallettrans;