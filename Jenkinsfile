pipeline {
    agent any
    stages {
        stage('Restore') { steps { sh 'dotnet restore tests/Meter.Tests' } }
        stage('Test') {
            steps { sh 'dotnet test tests/Meter.Tests -c Release --no-restore --logger "trx;LogFileName=tests.trx"' }
        }
        stage('Package') { steps { sh 'dotnet publish src/Meter.Api -c Release -o artifacts/api' } }
        stage('Dependency inventory') {
            steps { sh 'dotnet list tests/Meter.Tests package --vulnerable --include-transitive' }
        }
    }
    post { always { archiveArtifacts artifacts: '**/TestResults/*.trx', allowEmptyArchive: true } }
}
