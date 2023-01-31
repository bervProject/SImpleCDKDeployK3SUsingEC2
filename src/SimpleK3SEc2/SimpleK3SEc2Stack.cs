using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Constructs;

namespace SimpleK3SEc2
{
    public class SimpleK3SEc2Stack : Stack
    {
        internal SimpleK3SEc2Stack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            // 0.0 prepare the VPC
            var eksVpc = new Vpc(this, "eksVpc", new VpcProps()
            {
                MaxAzs = 3,
                IpAddresses = IpAddresses.Cidr("10.0.0.0/21"),
                SubnetConfiguration = new ISubnetConfiguration[]
                {
                    new SubnetConfiguration()
                    {
                        Name = "public",
                        SubnetType = SubnetType.PUBLIC,
                        CidrMask = 24
                    }
                }
            });
            // 0.1 security groups
            var securityGroups = new SecurityGroup(this, "eksSecurityGroup", new SecurityGroupProps()
            {
                Vpc = eksVpc,
                Description = "EKS Security Groups",
                AllowAllOutbound = true,
                DisableInlineRules = true
            });
            securityGroups.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(22), "Allow SSH");
            securityGroups.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(80), "Allow HTTP");
            securityGroups.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(5000), "Allow Application");
            securityGroups.AddIngressRule(Peer.AnyIpv4(), Port.Tcp(30007), "Allow Custom Application");
            // 0.2 Define Image
            var amazonLinuxImage = MachineImage.LatestAmazonLinux(new AmazonLinuxImageProps()
            {
                Generation = AmazonLinuxGeneration.AMAZON_LINUX_2,
                Edition = AmazonLinuxEdition.STANDARD,
                Virtualization = AmazonLinuxVirt.HVM,
                Storage = AmazonLinuxStorage.GENERAL_PURPOSE,
                CpuType = AmazonLinuxCpuType.X86_64
            });
            // 0.3 Volume
            var eksRootVolume = new BlockDevice()
            {
                DeviceName = "/dev/xvda",
                Volume = BlockDeviceVolume.Ebs(50),
            };
            // 0.4 User Data
            var userData =  UserData.ForLinux();
            userData.AddCommands("/opt/aws/bin/cfn-init -s WebTest --region us-east-1 -r NewServer");
            // 1.0 Define EC2
            var instance = new Instance_(this, "eksInstance", new InstanceProps()
            {
                Vpc = eksVpc,
                InstanceType = InstanceType.Of(InstanceClass.T3, InstanceSize.SMALL),
                MachineImage = amazonLinuxImage,
                BlockDevices = new IBlockDevice[]
                {
                  eksRootVolume  
                },
                Init = CloudFormationInit.FromConfigSets(new ConfigSetProps()
                {
                    ConfigSets = new Dictionary<string, string[]>()
                    {
                        {"default", new []{"yumPreinstall", "config"}}
                    },
                    Configs = new Dictionary<string, InitConfig>()
                    {
                        {"yumPreinstall", new InitConfig(new InitElement[]
                        {
                            InitPackage.Yum("curl"),
                            InitPackage.Yum("nginx")
                        })},
                        {"config", new InitConfig(new InitElement[]
                        {
                            InitCommand.ShellCommand("curl -sfL https://get.k3s.io | sh -"),
                            InitCommand.ShellCommand("k3s kubectl apply -f https://k8s.io/examples/controllers/nginx-deployment.yaml"),
                            InitCommand.ShellCommand("k3s kubectl apply -f https://gist.githubusercontent.com/berviantoleo/a03c2dcb3150764124a8c050124db136/raw/2de35a3880888fbbde5bec3360a2b1dc4770fbb9/nginx-service.yaml"),
                            InitService.Enable("nginx", new InitServiceOptions()
                            {
                                ServiceRestartHandle = new InitServiceRestartHandle()
                            })
                        })}
                    }
                }),
                InitOptions = new ApplyCloudFormationInitOptions()
                {
                    ConfigSets = new[]{"default"},
                    Timeout = Duration.Hours(1)
                },
            });
            var cfnOutput = new CfnOutput(this, "EC2PublicAddress", new CfnOutputProps()
            {
                Value = instance.InstancePublicIp
            });
        }
    }
}
